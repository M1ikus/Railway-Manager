cmake_minimum_required(VERSION 3.16)
project(RailwayManager VERSION 1.0.0 LANGUAGES CXX)

# Standard C++17
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Opcje kompilacji
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

# Flagi kompilacji
set(CMAKE_CXX_FLAGS "-Wall -Wextra")
set(CMAKE_CXX_FLAGS_DEBUG "-g -O0")
set(CMAKE_CXX_FLAGS_RELEASE "-O3")

# Znajdź pakiety
find_package(Qt6 REQUIRED COMPONENTS Core Widgets)
find_package(SDL2 REQUIRED)
find_package(SDL2_image REQUIRED)
find_package(SDL2_ttf REQUIRED)

# ImGui jako submoduł
add_subdirectory(external/imgui)

# nlohmann/json
include(FetchContent)
FetchContent_Declare(
    json
    GIT_REPOSITORY https://github.com/nlohmann/json.git
    GIT_TAG v3.11.2
)
FetchContent_MakeAvailable(json)

# Pliki źródłowe
set(SOURCES
    src/main.cpp
    src/core/Game.cpp
    src/core/GameState.cpp
    src/core/SaveManager.cpp
    
    src/data/DataLoader.cpp
    src/data/CSVLoader.cpp
    src/data/JSONLoader.cpp
    src/data/GeoJSONLoader.cpp
    
    src/models/Station.cpp
    src/models/Train.cpp
    src/models/Line.cpp
    src/models/Personnel.cpp
    src/models/Passenger.cpp
    src/models/Timetable.cpp
    src/models/Event.cpp
    
    src/simulation/SimulationEngine.cpp
    src/simulation/PassengerAI.cpp
    src/simulation/TrainMovement.cpp
    src/simulation/EconomyManager.cpp
    src/simulation/EventManager.cpp
    
    src/ui/MainWindow.cpp
    src/ui/Dashboard.cpp
    src/ui/MapWidget.cpp
    src/ui/TimetableEditor.cpp
    src/ui/FleetManager.cpp
    src/ui/FinancePanel.cpp
    src/ui/PersonnelPanel.cpp
    
    src/map/MapRenderer.cpp
    src/map/MapCamera.cpp
    src/map/TrainSprite.cpp
    src/map/StationMarker.cpp
    
    src/utils/Logger.cpp
    src/utils/Config.cpp
    src/utils/MathUtils.cpp
)

# Pliki nagłówkowe
set(HEADERS
    src/core/Game.h
    src/core/GameState.h
    src/core/SaveManager.h
    
    src/data/DataLoader.h
    src/data/CSVLoader.h
    src/data/JSONLoader.h
    src/data/GeoJSONLoader.h
    
    src/models/Station.h
    src/models/Train.h
    src/models/Line.h
    src/models/Personnel.h
    src/models/Passenger.h
    src/models/Timetable.h
    src/models/Event.h
    
    src/simulation/SimulationEngine.h
    src/simulation/PassengerAI.h
    src/simulation/TrainMovement.h
    src/simulation/EconomyManager.h
    src/simulation/EventManager.h
    
    src/ui/MainWindow.h
    src/ui/Dashboard.h
    src/ui/MapWidget.h
    src/ui/TimetableEditor.h
    src/ui/FleetManager.h
    src/ui/FinancePanel.h
    src/ui/PersonnelPanel.h
    
    src/map/MapRenderer.h
    src/map/MapCamera.h
    src/map/TrainSprite.h
    src/map/StationMarker.h
    
    src/utils/Logger.h
    src/utils/Config.h
    src/utils/MathUtils.h
)

# Automatyczne MOC dla Qt
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

# Utwórz wykonywany plik
add_executable(RailwayManager ${SOURCES} ${HEADERS})

# Dołącz katalogi
target_include_directories(RailwayManager PRIVATE
    ${CMAKE_SOURCE_DIR}/src
    ${SDL2_INCLUDE_DIRS}
    ${SDL2_IMAGE_INCLUDE_DIRS}
    ${SDL2_TTF_INCLUDE_DIRS}
)

# Linkuj biblioteki
target_link_libraries(RailwayManager
    Qt6::Core
    Qt6::Widgets
    ${SDL2_LIBRARIES}
    ${SDL2_IMAGE_LIBRARIES}
    ${SDL2_TTF_LIBRARIES}
    imgui
    nlohmann_json::nlohmann_json
)

# Kopiuj pliki danych do katalogu budowania
file(COPY ${CMAKE_SOURCE_DIR}/data DESTINATION ${CMAKE_BINARY_DIR})
file(COPY ${CMAKE_SOURCE_DIR}/assets DESTINATION ${CMAKE_BINARY_DIR})
file(COPY ${CMAKE_SOURCE_DIR}/lang DESTINATION ${CMAKE_BINARY_DIR})

# Instalacja
install(TARGETS RailwayManager DESTINATION bin)
install(DIRECTORY data DESTINATION share/railwaymanager)
install(DIRECTORY assets DESTINATION share/railwaymanager)
install(DIRECTORY lang DESTINATION share/railwaymanager)

# Testy (opcjonalne)
option(BUILD_TESTS "Build tests" OFF)
if(BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()