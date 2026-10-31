#pragma once

#include <array>
#include <atomic>
#include <cstddef>
#include <mutex>

namespace sophron {

// A ring of commands for the audio thread. Senders take turns under a lock, since the window and the
// MIDI driver both send; the audio thread is the only reader and never waits.
template <typename T, std::size_t Capacity>
class CommandQueue {
public:
    // False when the ring is full, which only a stalled audio thread would allow.
    bool push(const T& item) {
        std::lock_guard lock(senders_);
        const auto write = write_.load(std::memory_order_relaxed);
        const auto next = (write + 1) % Capacity;
        if (next == read_.load(std::memory_order_acquire)) {
            return false;
        }
        items_[write] = item;
        write_.store(next, std::memory_order_release);
        return true;
    }

    bool pop(T& item) {
        const auto read = read_.load(std::memory_order_relaxed);
        if (read == write_.load(std::memory_order_acquire)) {
            return false;
        }
        item = items_[read];
        read_.store((read + 1) % Capacity, std::memory_order_release);
        return true;
    }

private:
    std::array<T, Capacity> items_{};
    std::atomic<std::size_t> read_{0};
    std::atomic<std::size_t> write_{0};
    std::mutex senders_;
};

}  // namespace sophron
