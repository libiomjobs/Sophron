#include "sophron_engine.h"

#include <optional>

#include "command_queue.h"
#include "player.h"
#include "wasapi_output.h"

using sophron::Event;

// The engine as the C# side sees it: events go into the queue from any thread, and the audio thread
// hands them to the player at the start of each buffer, which plays each on its own sample.
struct SophronEngine final : sophron::AudioSource {
    sophron::CommandQueue<Event, 4096> events;
    std::optional<sophron::Player> player;
    sophron::WasapiOutput output;

    void prepare(double sampleRate) override { player.emplace(sampleRate); }

    void render(float* stereo, uint32_t frames) override {
        Event event;
        while (events.pop(event)) {
            player->submit(event);
        }
        player->render(stereo, frames);
    }

    void push(Event::Kind kind, int32_t id, double value, float velocity, int64_t when) {
        events.push({kind, id, value, velocity, when});
    }
};

extern "C" {

SophronEngine* sophron_create(void) { return new SophronEngine(); }

int32_t sophron_start(SophronEngine* engine) { return engine->output.start(*engine); }

void sophron_stop(SophronEngine* engine) { engine->output.stop(); }

double sophron_sample_rate(const SophronEngine* engine) { return engine->output.sampleRate(); }

void sophron_clock(const SophronEngine* engine, int64_t* sample, int64_t* qpc_100ns) {
    engine->output.clock(*sample, *qpc_100ns);
}

void sophron_note_on(SophronEngine* engine, int32_t note_id, double frequency_hz, float velocity) {
    engine->push(Event::Kind::NoteOn, note_id, frequency_hz, velocity, Event::Now);
}

void sophron_note_off(SophronEngine* engine, int32_t note_id) {
    engine->push(Event::Kind::NoteOff, note_id, 0, 0, Event::Now);
}

void sophron_all_notes_off(SophronEngine* engine) { engine->push(Event::Kind::AllNotesOff, 0, 0, 0, Event::Now); }

void sophron_sustain(SophronEngine* engine, int32_t down) {
    engine->push(Event::Kind::Sustain, 0, down != 0 ? 1.0 : 0.0, 0, Event::Now);
}

void sophron_pitch_bend(SophronEngine* engine, double cents) {
    engine->push(Event::Kind::PitchBend, 0, cents, 0, Event::Now);
}

void sophron_retune(SophronEngine* engine, double ratio) {
    engine->push(Event::Kind::Retune, 0, ratio, 0, Event::Now);
}

void sophron_note_on_at(SophronEngine* engine, int64_t sample, int32_t note_id, double frequency_hz, float velocity) {
    engine->push(Event::Kind::NoteOn, note_id, frequency_hz, velocity, sample);
}

void sophron_note_off_at(SophronEngine* engine, int64_t sample, int32_t note_id) {
    engine->push(Event::Kind::NoteOff, note_id, 0, 0, sample);
}

void sophron_sustain_at(SophronEngine* engine, int64_t sample, int32_t down) {
    engine->push(Event::Kind::Sustain, 0, down != 0 ? 1.0 : 0.0, 0, sample);
}

void sophron_pitch_bend_at(SophronEngine* engine, int64_t sample, double cents) {
    engine->push(Event::Kind::PitchBend, 0, cents, 0, sample);
}

void sophron_click_at(SophronEngine* engine, int64_t sample, int32_t accent) {
    engine->push(Event::Kind::Click, 0, accent != 0 ? 1.0 : 0.0, 0, sample);
}

void sophron_cancel_scheduled(SophronEngine* engine) {
    engine->push(Event::Kind::CancelScheduled, 0, 0, 0, Event::Now);
}

void sophron_destroy(SophronEngine* engine) {
    engine->output.stop();
    delete engine;
}

}  // extern "C"
