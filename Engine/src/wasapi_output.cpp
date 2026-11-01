#include "wasapi_output.h"

#include <windows.h>

#include <audioclient.h>
#include <avrt.h>
#include <ksmedia.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

namespace sophron {

namespace {

constexpr REFERENCE_TIME BufferDuration = 100'000;  // 10 ms, in units of 100 ns.
constexpr DWORD WakeTimeoutMs = 200;

// Asks for stereo float at the device's own rate; Windows converts if the device differs.
WAVEFORMATEXTENSIBLE stereoFloat(DWORD sampleRate) {
    WAVEFORMATEXTENSIBLE format{};
    format.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
    format.Format.nChannels = 2;
    format.Format.nSamplesPerSec = sampleRate;
    format.Format.wBitsPerSample = 32;
    format.Format.nBlockAlign = format.Format.nChannels * sizeof(float);
    format.Format.nAvgBytesPerSec = sampleRate * format.Format.nBlockAlign;
    format.Format.cbSize = sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX);
    format.Samples.wValidBitsPerSample = 32;
    format.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
    format.SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
    return format;
}

}  // namespace

WasapiOutput::~WasapiOutput() { stop(); }

void WasapiOutput::clock(int64_t& sample, int64_t& qpc100ns) const {
    uint32_t before = 0;
    uint32_t after = 0;
    do {
        before = clockSequence_.load(std::memory_order_acquire);
        sample = clockSample_.load(std::memory_order_relaxed);
        qpc100ns = clockTime_.load(std::memory_order_relaxed);
        std::atomic_thread_fence(std::memory_order_acquire);
        after = clockSequence_.load(std::memory_order_relaxed);
    } while ((before & 1) != 0 || before != after);
}

void WasapiOutput::publishClock(int64_t sample, int64_t qpc100ns) {
    const auto sequence = clockSequence_.load(std::memory_order_relaxed);
    clockSequence_.store(sequence + 1, std::memory_order_relaxed);
    std::atomic_thread_fence(std::memory_order_release);
    clockSample_.store(sample, std::memory_order_relaxed);
    clockTime_.store(qpc100ns, std::memory_order_relaxed);
    clockSequence_.store(sequence + 2, std::memory_order_release);
}

long WasapiOutput::start(AudioSource& source) {
    if (running_.load()) {
        return S_OK;
    }

    std::promise<long> opened;
    auto result = opened.get_future();
    running_.store(true);
    thread_ = std::thread([this, &source, &opened] { run(source, &opened); });

    const auto hr = result.get();
    if (FAILED(hr)) {
        running_.store(false);
        thread_.join();
    }
    return hr;
}

void WasapiOutput::stop() {
    running_.store(false);
    if (thread_.joinable()) {
        thread_.join();
    }
}

// Everything COM is made, used and released on this one thread.
void WasapiOutput::run(AudioSource& source, std::promise<long>* opened) {
    auto hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) {
        opened->set_value(hr);
        return;
    }

    HANDLE wake = nullptr;
    {
        ComPtr<IMMDeviceEnumerator> devices;
        ComPtr<IMMDevice> device;
        ComPtr<IAudioClient> client;
        ComPtr<IAudioRenderClient> renderer;
        ComPtr<IAudioClock> audioClock;
        UINT64 clockFrequency = 0;
        UINT32 bufferFrames = 0;

        auto open = [&]() -> HRESULT {
            auto r = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&devices));
            if (FAILED(r)) return r;
            r = devices->GetDefaultAudioEndpoint(eRender, eConsole, &device);
            if (FAILED(r)) return r;
            r = device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(client.GetAddressOf()));
            if (FAILED(r)) return r;

            WAVEFORMATEX* mix = nullptr;
            r = client->GetMixFormat(&mix);
            if (FAILED(r)) return r;
            const auto rate = mix->nSamplesPerSec;
            CoTaskMemFree(mix);

            auto format = stereoFloat(rate);
            r = client->Initialize(AUDCLNT_SHAREMODE_SHARED,
                                   AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
                                       AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                                   BufferDuration, 0, &format.Format, nullptr);
            if (FAILED(r)) return r;

            wake = CreateEventW(nullptr, FALSE, FALSE, nullptr);
            if (wake == nullptr) return HRESULT_FROM_WIN32(GetLastError());
            r = client->SetEventHandle(wake);
            if (FAILED(r)) return r;
            r = client->GetBufferSize(&bufferFrames);
            if (FAILED(r)) return r;
            r = client->GetService(IID_PPV_ARGS(&renderer));
            if (FAILED(r)) return r;
            r = client->GetService(IID_PPV_ARGS(&audioClock));
            if (FAILED(r)) return r;
            r = audioClock->GetFrequency(&clockFrequency);
            if (FAILED(r)) return r;

            sampleRate_.store(rate);
            source.prepare(rate);
            return client->Start();
        };

        hr = open();
        opened->set_value(hr);

        if (SUCCEEDED(hr)) {
            DWORD taskIndex = 0;
            const auto task = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);

            while (running_.load(std::memory_order_relaxed)) {
                WaitForSingleObject(wake, WakeTimeoutMs);

                UINT32 padding = 0;
                if (FAILED(client->GetCurrentPadding(&padding))) {
                    break;  // The device went away; the engine falls silent rather than crash.
                }
                const auto frames = bufferFrames - padding;
                if (frames == 0) {
                    continue;
                }

                BYTE* data = nullptr;
                if (FAILED(renderer->GetBuffer(frames, &data))) {
                    break;
                }
                source.render(reinterpret_cast<float*>(data), frames);
                renderer->ReleaseBuffer(frames, 0);

                // The device counts what it has played in its own units; in samples, it is the same count
                // the source has been rendering, since both begin when the stream does.
                UINT64 played = 0;
                UINT64 playedAt = 0;
                if (SUCCEEDED(audioClock->GetPosition(&played, &playedAt)) && clockFrequency != 0) {
                    const auto rate = sampleRate_.load(std::memory_order_relaxed);
                    publishClock(static_cast<int64_t>(static_cast<double>(played) * rate / static_cast<double>(clockFrequency)),
                                 static_cast<int64_t>(playedAt));
                }
            }

            client->Stop();
            if (task != nullptr) {
                AvRevertMmThreadCharacteristics(task);
            }
        }
    }

    if (wake != nullptr) {
        CloseHandle(wake);
    }
    CoUninitialize();
}

}  // namespace sophron
