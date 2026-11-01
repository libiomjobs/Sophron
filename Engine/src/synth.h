#pragma once

#include <array>
#include <cstdint>

namespace sophron {

// A plain polyphonic synth: a sawtooth softened by a low-pass, with a short attack and a gentle
// release. It is here to make the tuning audible, not to be the last instrument Sophron has.
class Synth {
public:
    static constexpr int MaxVoices = 32;

    explicit Synth(double sampleRate);

    void noteOn(int32_t id, double frequency, float velocity);
    void noteOff(int32_t id);
    void allNotesOff();

    // While the pedal is down, released notes go on sounding until it comes up.
    void setSustain(bool down);

    // Bends every note by `cents`, gliding there over a few milliseconds so the wheel does not step.
    void setBend(double cents);

    // Moves every sounding note by `ratio`, as when the fundamental is tuned while notes are held.
    void retune(double ratio);

    // A metronome click, brighter on the first beat of a bar. It sits beside the notes, not among
    // them: it takes no voice and is untouched by the bend.
    void click(bool accent);

    // Writes, rather than adds, `frames` frames of interleaved stereo.
    void render(float* stereo, uint32_t frames);

    int activeVoices() const;

private:
    struct Voice {
        int32_t id = 0;
        bool active = false;
        bool held = false;
        bool sustained = false;
        double phase = 0;
        double increment = 0;
        float velocity = 0;
        float level = 0;
        float filtered = 0;
        float filterCoefficient = 0;
        uint64_t started = 0;
    };

    Voice& voiceFor(int32_t id);

    std::array<Voice, MaxVoices> voices_{};
    double sampleRate_;
    float attackStep_;
    float releaseFactor_;
    double bendGlide_;
    double bend_ = 1;
    double bendTarget_ = 1;
    bool sustain_ = false;
    double clickPhase_ = 0;
    double clickIncrement_ = 0;
    float clickLevel_ = 0;
    float clickDecay_;
    uint64_t notesStarted_ = 0;
};

}  // namespace sophron
