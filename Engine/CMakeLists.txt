cmake_minimum_required(VERSION 3.25)
project(SophronEngine LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# The runtime goes inside the DLL, so Sophron runs on machines without the Visual C++ redistributable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_compile_options(/W4 /WX /permissive- /utf-8)
add_compile_definitions(UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)

# The synth and its player know nothing of devices, so the tests can run it without one.
add_library(sophron_synth STATIC src/synth.cpp src/player.cpp)
target_include_directories(sophron_synth PUBLIC src)

add_library(sophron_engine SHARED src/engine.cpp src/wasapi_output.cpp)
target_include_directories(sophron_engine PUBLIC include)
target_compile_definitions(sophron_engine PRIVATE SOPHRON_ENGINE_BUILD)
target_link_libraries(sophron_engine PRIVATE sophron_synth ole32 avrt)

enable_testing()
add_executable(engine_tests tests/synth_tests.cpp)
target_link_libraries(engine_tests PRIVATE sophron_synth)
add_test(NAME synth COMMAND engine_tests)
