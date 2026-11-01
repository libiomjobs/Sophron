// The engine's C interface, which Sophron's C# side calls. Every function may be called from any
// thread: it queues a command, and the audio thread acts on it at its next buffer.
#pragma once

#include <stdint.h>

#ifdef SOPHRON_ENGINE_BUILD
#define SOPHRON_API __declspec(dllexport)
#else
#define SOPHRON_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct SophronEngine SophronEngine;

SOPHRON_API SophronEngine* sophron_create(void);

// Opens the default output device and starts the audio thread. Returns 0, or the HRESULT that stopped it.
SOPHRON_API int32_t sophron_start(SophronEngine* engine);

SOPHRON_API void sophron_stop(SophronEngine* engine);

// The device's rate once started; 0 before.
SOPHRON_API double sophron_sample_rate(const SophronEngine* engine);

// The engine plays frequencies, not note numbers: the tuning is the caller's business.
SOPHRON_API void sophron_note_on(SophronEngine* engine, int32_t note_id, double frequency_hz, float velocity);

SOPHRON_API void sophron_note_off(SophronEngine* engine, int32_t note_id);

SOPHRON_API void sophron_all_notes_off(SophronEngine* engine);

// The sustain pedal: nonzero is down.
SOPHRON_API void sophron_sustain(SophronEngine* engine, int32_t down);

// Bends every note by this many cents; how far the wheel reaches is the caller's business, as the tuning is.
SOPHRON_API void sophron_pitch_bend(SophronEngine* engine, double cents);

// Moves every sounding note by this frequency ratio, so held notes follow a change of fundamental.
SOPHRON_API void sophron_retune(SophronEngine* engine, double ratio);

// The sample the listener is hearing, counted from the start of the output, and when it was heard,
// in the 100 ns units of QueryPerformanceCounter. Both are 0 until the device has played anything.
SOPHRON_API void sophron_clock(const SophronEngine* engine, int64_t* sample, int64_t* qpc_100ns);

// The same, on a sample of the clock above rather than at once. Events due at one sample are played in
// the order they were sent; one sent too late to be on time is played as soon as it can be.
SOPHRON_API void sophron_note_on_at(SophronEngine* engine, int64_t sample, int32_t note_id, double frequency_hz, float velocity);
SOPHRON_API void sophron_note_off_at(SophronEngine* engine, int64_t sample, int32_t note_id);
SOPHRON_API void sophron_sustain_at(SophronEngine* engine, int64_t sample, int32_t down);
SOPHRON_API void sophron_pitch_bend_at(SophronEngine* engine, int64_t sample, double cents);

// A metronome click; nonzero accent marks the first beat of a bar.
SOPHRON_API void sophron_click_at(SophronEngine* engine, int64_t sample, int32_t accent);

// Forgets every event still waiting for its sample, as when the transport stops.
SOPHRON_API void sophron_cancel_scheduled(SophronEngine* engine);

// Stops the engine if it is running, then frees it.
SOPHRON_API void sophron_destroy(SophronEngine* engine);

#ifdef __cplusplus
}
#endif
