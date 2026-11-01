#pragma once

#include <cstdint>
#include <vector>

#include "synth.h"

namespace sophron {

// Something for the synth to do, now or at a given sample.
struct Event {
    enum class Kind : uint8_t { NoteOn, NoteOff, AllNotesOff, Sustain, PitchBend, Retune, Click, CancelScheduled };

    static constexpr int64_t Now = -1;

    Kind kind = Kind::AllNotesOff;
    int32_t id = 0;
    // A note's frequency in hertz, the bend in cents, a retuning ratio, the pedal as 0 or 1, or 1 for an
    // accented click.
    double value = 0;
    float velocity = 0;
    // The sample to act on, counted from the start of the output, or Now.
    int64_t when = Now;
};

// Plays events into the synth on the very sample they name, splitting a buffer where one falls. Events
// due at the same sample are played in the order they came, so a note-off and a note-on for the same
// id at one tick stop the old note before starting the new one.
class Player {
public:
    static constexpr size_t MaxScheduled = 8192;

    explicit Player(double sampleRate);

    // On the audio thread, as each event arrives. One that is due, or already late, is played at the
    // start of the next buffer.
    void submit(const Event& event);

    void render(float* stereo, uint32_t frames);

    // The first sample of the next buffer.
    int64_t position() const { return position_; }

    Synth& synth() { return synth_; }

private:
    void play(const Event& event);
    void playDue(int64_t now);

    Synth synth_;
    std::vector<Event> scheduled_;
    int64_t position_ = 0;
};

}  // namespace sophron
