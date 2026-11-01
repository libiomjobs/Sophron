#include "sophron_engine.h"

#include <memory>
#include <optional>

#include "command_queue.h"
#include "synth.h"
#include "wasapi_output.h"

namespace {

struct Command {
    enum class Kind : uint8_t { NoteOn, NoteOff, AllNotesOff, Sustain, PitchBend, Retune };

    Kind kind = Kind::AllNotesOff;
    int32_t id = 0;
    // A note's frequency in hertz, the bend in cents, a retuning ratio, or the pedal as 0 or 1.
    double value = 0;
    float velocity = 0;
};

}  // namespace

// The engine as the C# side sees it: commands go into the queue from any thread, and the audio
// thread plays them into the synth at the start of each buffer.
struct SophronEngine final : sophron::AudioSource {
    sophron::CommandQueue<Command, 1024> commands;
    std::optional<sophron::Synth> synth;
    sophron::WasapiOutput output;

    void prepare(double sampleRate) override { synth.emplace(sampleRate); }

    void render(float* stereo, uint32_t frames) override {
        Command command;
        while (commands.pop(command)) {
            switch (command.kind) {
                case Command::Kind::NoteOn:
                    synth->noteOn(command.id, command.value, command.velocity);
                    break;
                case Command::Kind::NoteOff:
                    synth->noteOff(command.id);
                    break;
                case Command::Kind::AllNotesOff:
                    synth->allNotesOff();
                    break;
                case Command::Kind::Sustain:
                    synth->setSustain(command.value != 0);
                    break;
                case Command::Kind::PitchBend:
                    synth->setBend(command.value);
                    break;
                case Command::Kind::Retune:
                    synth->retune(command.value);
                    break;
            }
        }
        synth->render(stereo, frames);
    }
};

extern "C" {

SophronEngine* sophron_create(void) { return new SophronEngine(); }

int32_t sophron_start(SophronEngine* engine) { return engine->output.start(*engine); }

void sophron_stop(SophronEngine* engine) { engine->output.stop(); }

double sophron_sample_rate(const SophronEngine* engine) { return engine->output.sampleRate(); }

void sophron_note_on(SophronEngine* engine, int32_t note_id, double frequency_hz, float velocity) {
    engine->commands.push({Command::Kind::NoteOn, note_id, frequency_hz, velocity});
}

void sophron_note_off(SophronEngine* engine, int32_t note_id) {
    engine->commands.push({Command::Kind::NoteOff, note_id, 0, 0});
}

void sophron_all_notes_off(SophronEngine* engine) { engine->commands.push({}); }

void sophron_sustain(SophronEngine* engine, int32_t down) {
    engine->commands.push({Command::Kind::Sustain, 0, down != 0 ? 1.0 : 0.0, 0});
}

void sophron_pitch_bend(SophronEngine* engine, double cents) {
    engine->commands.push({Command::Kind::PitchBend, 0, cents, 0});
}

void sophron_retune(SophronEngine* engine, double ratio) {
    engine->commands.push({Command::Kind::Retune, 0, ratio, 0});
}

void sophron_destroy(SophronEngine* engine) {
    engine->output.stop();
    delete engine;
}

}  // extern "C"
