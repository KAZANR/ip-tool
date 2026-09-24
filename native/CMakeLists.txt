cmake_minimum_required(VERSION 3.14)

project(NetworkDoctorNeo LANGUAGES C CXX)

include(FetchContent)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(EUI_WINDOW_BACKEND "glfw" CACHE STRING "EUI-NEO window backend")
set(EUI_RENDER_BACKEND "opengl" CACHE STRING "EUI-NEO render backend")
set(EUI_ENABLE_TRAY OFF CACHE BOOL "Disable EUI-NEO tray integration")

set(eui_neo_repository "https://atomgit.com/sudoevolve/EUI-NEO.git")
execute_process(
    COMMAND git ls-remote "https://github.com/sudoevolve/EUI-NEO.git" HEAD
    RESULT_VARIABLE eui_neo_github_status
    OUTPUT_QUIET
    ERROR_QUIET
    TIMEOUT 8
)
if(NOT eui_neo_github_status EQUAL 0)
    set(eui_neo_repository "https://atomgit.com/sudoevolve/EUI-NEO.git")
endif()

FetchContent_Declare(
    eui_neo
    GIT_REPOSITORY ${eui_neo_repository}
    GIT_TAG main
    GIT_SHALLOW TRUE
)

FetchContent_Declare(
    nlohmann_json
    GIT_REPOSITORY https://gitee.com/mirrors/nlohmann-json.git
    GIT_TAG v3.11.3
    GIT_SHALLOW TRUE
)

set(JSON_BuildTests OFF CACHE INTERNAL "")
set(JSON_Install OFF CACHE INTERNAL "")

FetchContent_MakeAvailable(eui_neo nlohmann_json)

enable_testing()

add_executable(NetworkDoctorNeo
    main.cpp
    app_state.cpp
    cli_client.cpp
    ui_theme.cpp
)

eui_neo_configure_app(NetworkDoctorNeo)

target_link_libraries(NetworkDoctorNeo PRIVATE nlohmann_json::nlohmann_json)

if(WIN32)
    target_link_libraries(NetworkDoctorNeo PRIVATE shell32)
endif()

if(MSVC)
    target_compile_options(NetworkDoctorNeo PRIVATE /utf-8)
endif()

add_executable(NetworkDoctorNativeTests
    tests/native_tests.cpp
    app_state.cpp
    cli_client.cpp
)

target_link_libraries(NetworkDoctorNativeTests PRIVATE eui::neo nlohmann_json::nlohmann_json)
target_include_directories(NetworkDoctorNativeTests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

if(WIN32)
    target_link_libraries(NetworkDoctorNativeTests PRIVATE shell32)
endif()

if(MSVC)
    target_compile_options(NetworkDoctorNativeTests PRIVATE /utf-8)
endif()

add_test(NAME NetworkDoctorNativeTests COMMAND NetworkDoctorNativeTests)
