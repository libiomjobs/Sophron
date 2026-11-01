#include "player.h"

#include <algorithm>

namespace sophron {

Player::Player(double sampleRate) : synth_(sampleRate) { scheduled_.reserve(MaxScheduled); }

void Player::submit(const Event& event) {
    if (event.kind == Event::Kind::CancelScheduled) {
        scheduled_.clear();
    } else if (event.when <= position_) {
        play(event);
    } else if (scheduled_.size() < MaxScheduled) {
        scheduled_.push_back(event);
    } else {
        play(event);  // Better early than never; the queue is far deeper than any lookahead needs.
    }
}

void Player::render(float* stereo, uint32_t frames) {
    uint32_t done = 0;
    for (;;) {
        const auto now = position_ + done;
        playDue(now);
        if (done == frames) {
            break;
        }

        auto next = position_ + frames;
        for (const auto& event : scheduled_) {
            next = std::min(next, event.when);
        }

        const auto count = static_cast<uint32_t>(next - now);
        synth_.render(stereo + 2 * static_cast<size_t>(done), count);
        done += count;
    }
    position_ += frames;
}

// Plays the events due by `now` in the order they arrived, keeping the rest in theirs.
void Player::playDue(int64_t now) {
    size_t kept = 0;
    for (size_t i = 0; i < scheduled_.size(); ++i) {
        if (scheduled_[i].when <= now) {
            play(scheduled_[i]);
        } else {
            scheduled_[kept++] = scheduled_[i];
        }
    }
    scheduled_.resize(kept);
}

void Player::play(const Event& event) {
    switch (event.kind) {
        case Event::Kind::NoteOn:
            synth_.noteOn(event.id, event.value, event.velocity);
            break;
        case Event::Kind::NoteOff:
            synth_.noteOff(event.id);
            break;
        case Event::Kind::AllNotesOff:
            synth_.allNotesOff();
            break;
        case Event::Kind::Sustain:
            synth_.setSustain(event.value != 0);
            break;
        case Event::Kind::PitchBend:
            synth_.setBend(event.value);
            break;
        case Event::Kind::Retune:
            synth_.retune(event.value);
            break;
        case Event::Kind::Click:
            synth_.click(event.value != 0);
            break;
        case Event::Kind::CancelScheduled:
            break;
    }
}

}  // namespace sophron
