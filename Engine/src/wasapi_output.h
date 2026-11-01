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

    // The sample the listener is hearing, counted as the source renders them, and the moment it was
    // heard, in the 100 ns units of QueryPerformanceCounter. Both are 0 until the device has played.
    void clock(int64_t& sample, int64_t& qpc100ns) const;

private:
    void run(AudioSource& source, std::promise<long>* opened);
    void publishClock(int64_t sample, int64_t qpc100ns);

    std::thread thread_;
    std::atomic<bool> running_{false};
    std::atomic<double> sampleRate_{0};

    // A sequence lock: odd while the audio thread is writing the pair, so a reader can tell to try again.
    std::atomic<uint32_t> clockSequence_{0};
    std::atomic<int64_t> clockSample_{0};
    std::atomic<int64_t> clockTime_{0};
};

}  // namespace sophron
