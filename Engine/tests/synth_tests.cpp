// The synth's tests: it plays the frequency it is given, lets go when told, and survives more notes
// than it has voices. A plain executable, so CTest needs nothing more than its exit code.
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <vector>

#include "player.h"
#include "synth.h"

namespace {

constexpr double Rate = 48'000;
int failures = 0;

void check(bool ok, const char* what) {
    std::printf("%s %s\n", ok ? "pass" : "FAIL", what);
    if (!ok) ++failures;
}

std::vector<float> render(sophron::Synth& synth, double seconds) {
    std::vector<float> stereo(static_cast<size_t>(seconds * Rate) * 2);
    synth.render(stereo.data(), static_cast<uint32_t>(stereo.size() / 2));
    return stereo;
}

// A sawtooth crosses zero going up once a cycle, so counting those over a second gives the pitch.
int risingCrossings(const std::vector<float>& stereo) {
    int count = 0;
    for (size_t i = 2; i < stereo.size(); i += 2) {
        if (stereo[i - 2] < 0 && stereo[i] >= 0) ++count;
    }
    return count;
}

}  // namespace

int main() {
    {
        sophron::Synth synth(Rate);
        synth.noteOn(108, 432.0, 1.0f);
        render(synth, 0.1);  // Past the attack.
        const auto crossings = risingCrossings(render(synth, 1.0));
        std::printf("     432 Hz gave %d rising crossings in a second\n", crossings);
        check(std::abs(crossings - 432) <= 1, "a note plays at the frequency it was given");
    }
    {
        sophron::Synth synth(Rate);
        synth.noteOn(1, 27.0 * std::pow(2.0, 109 / 27.0), 1.0f);
        const auto crossings = risingCrossings(render(synth, 1.0));
        check(std::abs(crossings - 444) <= 1, "the step above A4 plays at 443.3 Hz, not a 12-TET pitch");
    }
    {
        sophron::Synth synth(Rate);
        synth.noteOn(7, 300.0, 1.0f);
        render(synth, 0.2);
        synth.noteOff(7);
        const auto tail = render(synth, 3.0);
        check(synth.activeVoices() == 0, "a released note dies away");
        check(std::abs(tail[tail.size() - 1]) < 1e-3f, "and leaves silence behind it");
    }
    {
        sophron::Synth synth(Rate);
        for (int i = 0; i < 40; ++i) {
            synth.noteOn(i, 100.0 + i * 10, 1.0f);
            render(synth, 0.01);
        }
        auto loudest = 0.0f;
        for (const auto sample : render(synth, 0.5)) loudest = std::max(loudest, std::abs(sample));
        check(synth.activeVoices() == sophron::Synth::MaxVoices, "forty notes share the thirty-two voices");
        check(loudest <= 1.0f, "and stay within full scale");
    }
    {
        sophron::Synth synth(Rate);
        synth.noteOn(5, 200.0, 1.0f);
        synth.noteOn(5, 200.0, 1.0f);
        check(synth.activeVoices() == 1, "the same note again reuses its voice");
    }
    {
        sophron::Synth synth(Rate);
        synth.setSustain(true);
        synth.noteOn(3, 300.0, 1.0f);
        render(synth, 0.1);
        synth.noteOff(3);
        render(synth, 3.0);
        check(synth.activeVoices() == 1, "the pedal holds a released note");
        synth.setSustain(false);
        render(synth, 3.0);
        check(synth.activeVoices() == 0, "and lets it go when it comes up");
    }
    {
        sophron::Synth synth(Rate);
        synth.setSustain(true);
        synth.noteOn(3, 300.0, 1.0f);
        synth.setSustain(false);
        render(synth, 3.0);
        check(synth.activeVoices() == 1, "lifting the pedal does not stop a key still held");
    }
    {
        sophron::Synth synth(Rate);
        synth.noteOn(108, 432.0, 1.0f);
        synth.setBend(1200.0 / 27 * 27);
        render(synth, 0.1);  // Past the glide.
        const auto crossings = risingCrossings(render(synth, 1.0));
        std::printf("     432 Hz bent up an octave gave %d rising crossings in a second\n", crossings);
        check(std::abs(crossings - 864) <= 1, "bending by an octave doubles the frequency");

        synth.setBend(-1200.0 / 27 * 3);
        render(synth, 0.1);
        const auto down = risingCrossings(render(synth, 1.0));
        check(std::abs(down - 400) <= 1, "and bending down a white key lands on the white below (400 Hz)");
    }
    {
        sophron::Synth synth(Rate);
        synth.noteOn(108, 432.0, 1.0f);
        render(synth, 0.1);
        synth.retune(440.0 / 432.0);
        const auto crossings = risingCrossings(render(synth, 1.0));
        check(std::abs(crossings - 440) <= 1, "retuning moves a held note from 432 to 440 Hz");
    }
    {
        sophron::Player player(Rate);
        player.submit({sophron::Event::Kind::NoteOn, 1, 432.0, 1.0f, 1000});
        std::vector<float> stereo(2 * 512);
        player.render(stereo.data(), 512);
        bool silent = true;
        for (auto sample : stereo) silent = silent && sample == 0;
        check(silent, "a note scheduled for sample 1000 is not heard in the first buffer");

        player.render(stereo.data(), 512);
        size_t first = 0;
        while (first < 512 && stereo[2 * first] == 0) ++first;
        std::printf("     first sound at sample %zu\n", 512 + first);
        check(512 + first == 1000 || 512 + first == 1001, "and starts on its very sample, in the middle of the next");
    }
    {
        sophron::Player player(Rate);
        player.submit({sophron::Event::Kind::NoteOn, 1, 300.0, 1.0f, sophron::Event::Now});
        player.submit({sophron::Event::Kind::NoteOff, 1, 0, 0, 2000});
        player.submit({sophron::Event::Kind::NoteOn, 1, 300.0, 1.0f, 2000});
        std::vector<float> stereo(2 * 48'000);
        player.render(stereo.data(), 48'000);
        check(player.synth().activeVoices() == 1, "an off and an on at one sample play in the order sent");
    }
    {
        sophron::Player player(Rate);
        player.submit({sophron::Event::Kind::NoteOn, 1, 300.0, 1.0f, 100});
        player.submit({sophron::Event::Kind::CancelScheduled, 0, 0, 0, sophron::Event::Now});
        std::vector<float> stereo(2 * 512);
        player.render(stereo.data(), 512);
        check(player.synth().activeVoices() == 0, "cancelling forgets what was waiting");
        check(player.position() == 512, "the player counts the samples it has made");
    }
    {
        sophron::Player player(Rate);
        player.submit({sophron::Event::Kind::Click, 0, 1, 0, 256});
        std::vector<float> stereo(2 * 4800);
        player.render(stereo.data(), 4800);
        float loudest = 0;
        for (auto sample : stereo) loudest = std::max(loudest, std::abs(sample));
        check(stereo[2 * 255] == 0 && loudest > 0.1f, "a click sounds when it is due");
        check(std::abs(stereo[2 * 4799]) < 1e-3f, "and is gone within a tenth of a second");
    }

    std::printf("%d failed\n", failures);
    return failures == 0 ? 0 : 1;
}
