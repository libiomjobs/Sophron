#pragma once

#include <atomic>
#include <cstdint>
#include <future>
#include <thread>

namespace sophron {

// What the output asks of whoever makes the sound.
class AudioSource {
public:
    virtual ~AudioSource() = default;

    // Called on the audio thread once the device's rate is known, before the first render.
    virtual void prepare(double sampleRate) = 0;

    // Called on the audio thread for every buffer: `frames` frames of interleaved stereo float.
    virtual void render(float* stereo, uint32_t frames) = 0;
};

// The default output device through WASAPI in shared mode, woken by the device for each buffer. The
// audio thread runs as a "Pro Audio" task so Windows schedules it ahead of everything else.
class WasapiOutput {
public:
    ~WasapiOutput();

    // Opens the device and starts the thread; returns S_OK, or the HRESULT that stopped it.
    long start(AudioSource& source);
    void stop();

    double sampleRate() const { return sampleRate_.load(); }

private:
    void run(AudioSource& source, std::promise<long>* opened);

    std::thread thread_;
    std::atomic<bool> running_{false};
    std::atomic<double> sampleRate_{0};
};

}  // namespace sophron
