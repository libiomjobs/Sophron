#include "synth.h"

#include <algorithm>
#include <cmath>
#include <numbers>

namespace sophron {

namespace {

constexpr double AttackSeconds = 0.005;
constexpr double ReleaseSeconds = 0.25;
constexpr double BendGlideSeconds = 0.005;
constexpr float Silent = 1e-4f;
constexpr float Gain = 0.25f;

// Smooths the jump in a sawtooth across one sample, which keeps high notes from aliasing.
double polyBlep(double phase, double increment) {
    if (phase < increment) {
        const auto t = phase / increment;
        return t + t - t * t - 1;
    }
    if (phase > 1 - increment) {
        const auto t = (phase - 1) / increment;
        return t * t + t + t + 1;
    }
    return 0;
}

}  // namespace

Synth::Synth(double sampleRate)
    : sampleRate_(sampleRate),
      attackStep_(static_cast<float>(1 / (AttackSeconds * sampleRate))),
      releaseFactor_(static_cast<float>(std::exp(-1 / (ReleaseSeconds * sampleRate)))),
      bendGlide_(1 - std::exp(-1 / (BendGlideSeconds * sampleRate))) {}

void Synth::noteOn(int32_t id, double frequency, float velocity) {
    auto& voice = voiceFor(id);
    voice.id = id;
    voice.active = true;
    voice.held = true;
    voice.sustained = false;
    voice.phase = 0;
    voice.increment = frequency / sampleRate_;
    voice.velocity = std::clamp(velocity, 0.0f, 1.0f);
    voice.level = 0;
    voice.filtered = 0;
    voice.started = ++notesStarted_;

    // The filter opens with the note, so every note has the same few harmonics and low ones are not dull.
    const auto cutoff = std::min(frequency * 6, sampleRate_ * 0.45);
    voice.filterCoefficient = static_cast<float>(1 - std::exp(-2 * std::numbers::pi * cutoff / sampleRate_));
}

void Synth::noteOff(int32_t id) {
    for (auto& voice : voices_) {
        if (voice.active && voice.held && voice.id == id) {
            voice.held = false;
            voice.sustained = sustain_;
        }
    }
}

void Synth::allNotesOff() {
    for (auto& voice : voices_) {
        voice.held = false;
        voice.sustained = false;
    }
}

void Synth::setSustain(bool down) {
    sustain_ = down;
    if (!down) {
        for (auto& voice : voices_) {
            voice.sustained = false;
        }
    }
}

void Synth::setBend(double cents) { bendTarget_ = std::exp2(cents / 1200); }

void Synth::render(float* stereo, uint32_t frames) {
    for (uint32_t frame = 0; frame < frames; ++frame) {
        bend_ += (bendTarget_ - bend_) * bendGlide_;

        float sum = 0;
        for (auto& voice : voices_) {
            if (!voice.active) {
                continue;
            }

            if (voice.held || voice.sustained) {
                voice.level = std::min(1.0f, voice.level + attackStep_);
            } else {
                voice.level *= releaseFactor_;
                if (voice.level < Silent) {
                    voice.active = false;
                    continue;
                }
            }

            const auto increment = voice.increment * bend_;
            const auto saw = static_cast<float>(2 * voice.phase - 1 - polyBlep(voice.phase, increment));
            voice.filtered += voice.filterCoefficient * (saw - voice.filtered);
            sum += voice.filtered * voice.level * voice.velocity;

            voice.phase += increment;
            if (voice.phase >= 1) {
                voice.phase -= 1;
            }
        }

        // Many notes at once are rounded off rather than clipped.
        const auto out = std::tanh(sum * Gain);
        stereo[2 * frame] = out;
        stereo[2 * frame + 1] = out;
    }
}

int Synth::activeVoices() const {
    return static_cast<int>(std::count_if(voices_.begin(), voices_.end(), [](const Voice& v) { return v.active; }));
}

// The same note again takes its own voice back; otherwise a free voice, then the quietest released
// one, then the oldest.
Synth::Voice& Synth::voiceFor(int32_t id) {
    for (auto& voice : voices_) {
        if (voice.active && voice.id == id) {
            return voice;
        }
    }
    for (auto& voice : voices_) {
        if (!voice.active) {
            return voice;
        }
    }

    Voice* quietest = nullptr;
    for (auto& voice : voices_) {
        if (!voice.held && !voice.sustained && (quietest == nullptr || voice.level < quietest->level)) {
            quietest = &voice;
        }
    }
    if (quietest != nullptr) {
        return *quietest;
    }

    return *std::min_element(voices_.begin(), voices_.end(), [](const Voice& a, const Voice& b) { return a.started < b.started; });
}

}  // namespace sophron
