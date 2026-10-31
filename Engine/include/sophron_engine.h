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

// Stops the engine if it is running, then frees it.
SOPHRON_API void sophron_destroy(SophronEngine* engine);

#ifdef __cplusplus
}
#endif
