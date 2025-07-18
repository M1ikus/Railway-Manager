#include "MapRenderer.h"
#include "MapCamera.h"
#include "core/Game.h"
#include "core/GameState.h"
#include "models/Station.h"
#include "models/Train.h"
#include "models/Line.h"
#include "utils/Logger.h"

#include <SDL2/SDL_image.h>
#include <SDL2/SDL_ttf.h>
#include <cmath>
#include <algorithm>

MapRenderer::MapRenderer(Game* game) : game(game) {
    if (game) {
        gameState = game->getGameState();
    }
    
    camera = std::make_unique<MapCamera>();
    
    // Inicjalizuj warstwy
    layers["terrain"] = {true, 0};
    layers["water"] = {true, 1};
    layers["lines"] = {true, 2};
    layers["stations"] = {true, 3};
    layers["trains"] = {true, 4};
    layers["signals"] = {false, 5};
    layers["labels"] = {true, 6};
    layers["grid"] = {false, 7};
}

MapRenderer::~MapRenderer() {
    cleanup();
}

bool MapRenderer::initialize(SDL_Renderer* renderer) {
    this->renderer = renderer;
    
    if (!renderer) {
        LOG_ERROR("Renderer SDL nie został ustawiony");
        return false;
    }
    
    // Załaduj tekstury
    if (!loadTextures()) {
        LOG_ERROR("Nie udało się załadować tekstur");
        return false;
    }
    
    // Załaduj czcionki
    if (!loadFonts()) {
        LOG_ERROR("Nie udało się załadować czcionek");
        return false;
    }
    
    // Ustaw viewport kamery
    int w, h;
    SDL_GetRendererOutputSize(renderer, &w, &h);
    camera->setViewport(w, h);
    
    LOG_INFO("MapRenderer zainicjalizowany");
    return true;
}

void MapRenderer::cleanup() {
    // Zwolnij tekstury
    for (auto& [name, texture] : textures) {
        if (texture) {
            SDL_DestroyTexture(texture);
        }
    }
    textures.clear();
    
    // Zwolnij czcionki
    if (labelFont) {
        TTF_CloseFont(labelFont);
        labelFont = nullptr;
    }
    if (debugFont) {
        TTF_CloseFont(debugFont);
        debugFont = nullptr;
    }
}

void MapRenderer::render() {
    if (!renderer) return;
    
    // Czyść ekran
    SDL_SetRenderDrawColor(renderer, backgroundColor.r, backgroundColor.g, 
                          backgroundColor.b, backgroundColor.a);
    SDL_RenderClear(renderer);
    
    // Renderuj warstwy w kolejności
    renderBackground();
    
    if (layers["terrain"].visible) renderTerrain();
    if (layers["water"].visible) renderWater();
    if (layers["grid"].visible && showGrid) renderGrid();
    if (layers["lines"].visible) renderLines();
    if (layers["stations"].visible) renderStations();
    if (layers["signals"].visible) renderSignals();
    if (layers["trains"].visible) renderTrains();
    if (layers["labels"].visible && showLabels) renderLabels();
    
    renderSelection();
    
    if (showDebugInfo) renderDebugInfo();
    
    // Prezentuj
    SDL_RenderPresent(renderer);
}

void MapRenderer::update() {
    // Aktualizuj bounds jeśli potrzeba
    if (!boundsCalculated && gameState) {
        bounds = calculateBounds();
        boundsCalculated = true;
    }
}

void MapRenderer::renderBackground() {
    // Tło mapy w zależności od typu
    switch (mapType) {
        case 0: // Standard
            SDL_SetRenderDrawColor(renderer, 245, 245, 245, 255);
            break;
        case 1: // Satellite
            SDL_SetRenderDrawColor(renderer, 20, 30, 20, 255);
            break;
        case 2: // Schematic
            SDL_SetRenderDrawColor(renderer, 255, 255, 255, 255);
            break;
        case 3: // Topographic
            SDL_SetRenderDrawColor(renderer, 240, 235, 225, 255);
            break;
    }
    
    SDL_RenderClear(renderer);
}

void MapRenderer::renderTerrain() {
    // TODO: Renderowanie terenu z danych GeoJSON
}

void MapRenderer::renderWater() {
    // TODO: Renderowanie wody (rzeki, jeziora)
}

void MapRenderer::renderGrid() {
    if (!showGrid) return;
    
    SDL_SetRenderDrawBlendMode(renderer, SDL_BLENDMODE_BLEND);
    SDL_SetRenderDrawColor(renderer, gridColor.r, gridColor.g, 
                          gridColor.b, gridColor.a);
    
    // Oblicz rozmiar siatki w zależności od zoomu
    float gridSize = 50.0f * camera->getZoom();
    
    int width = camera->getViewportWidth();
    int height = camera->getViewportHeight();
    
    // Linie pionowe
    for (int x = 0; x < width; x += gridSize) {
        SDL_RenderDrawLine(renderer, x, 0, x, height);
    }
    
    // Linie poziome
    for (int y = 0; y < height; y += gridSize) {
        SDL_RenderDrawLine(renderer, 0, y, width, y);
    }
}

void MapRenderer::renderLines() {
    if (!gameState) return;
    
    const auto& lines = gameState->getAllLines();
    
    for (const auto& line : lines) {
        renderLine(line.get());
    }
}

void MapRenderer::renderLine(const Line* line) {
    if (!line) return;
    
    // Kolor linii w zależności od typu
    SDL_Color color;
    int width = lineWidth;
    
    switch (line->getType()) {
        case LineType::MAIN:
            color = {0, 0, 200, 255}; // Niebieski
            width = 4;
            break;
        case LineType::REGIONAL:
            color = {0, 128, 255, 255}; // Jasnoniebieski
            width = 3;
            break;
        case LineType::LOCAL:
            color = {128, 128, 255, 255}; // Bardzo jasnoniebieski
            width = 2;
            break;
        case LineType::HIGH_SPEED:
            color = {255, 0, 0, 255}; // Czerwony
            width = 5;
            break;
        case LineType::INDUSTRIAL:
            color = {128, 128, 128, 255}; // Szary
            width = 2;
            break;
    }
    
    // Renderuj sekcje linii
    const auto& sections = line->getSections();
    for (const auto& section : sections) {
        auto fromStation = gameState->getStation(section.fromStationId);
        auto toStation = gameState->getStation(section.toStationId);
        
        if (fromStation && toStation) {
            renderLineSection(fromStation->getLatitude(), fromStation->getLongitude(),
                            toStation->getLatitude(), toStation->getLongitude(),
                            color, width);
        }
    }
}

void MapRenderer::renderLineSection(double lat1, double lon1, double lat2, double lon2,
                                   const SDL_Color& color, int width) {
    int x1, y1, x2, y2;
    camera->worldToScreen(lat1, lon1, x1, y1);
    camera->worldToScreen(lat2, lon2, x2, y2);
    
    // Sprawdź czy linia jest w widoku
    if (!isInView(lat1, lon1) && !isInView(lat2, lon2)) {
        return;
    }
    
    drawLine(x1, y1, x2, y2, color, width);
}

void MapRenderer::renderStations() {
    if (!gameState) return;
    
    const auto& stations = gameState->getAllStations();
    
    for (const auto& station : stations) {
        if (isInView(station->getLatitude(), station->getLongitude())) {
            renderStation(station.get());
        }
    }
}

void MapRenderer::renderStation(const Station* station) {
    if (!station) return;
    
    int x, y;
    camera->worldToScreen(station->getLatitude(), station->getLongitude(), x, y);
    
    // Rozmiar i kolor w zależności od typu
    int size = stationSize;
    SDL_Color color;
    
    switch (station->getType()) {
        case StationType::MAJOR:
            color = {255, 0, 0, 255}; // Czerwony
            size = stationSize * 2;
            break;
        case StationType::REGIONAL:
            color = {255, 128, 0, 255}; // Pomarańczowy
            size = stationSize * 1.5;
            break;
        case StationType::LOCAL:
            color = {255, 255, 0, 255}; // Żółty
            size = stationSize;
            break;
        case StationType::TECHNICAL:
            color = {128, 128, 128, 255}; // Szary
            size = stationSize * 0.8;
            break;
        case StationType::FREIGHT:
            color = {139, 69, 19, 255}; // Brązowy
            size = stationSize * 1.2;
            break;
    }
    
    // Rysuj stację
    drawFilledCircle(x, y, size, color);
    drawCircle(x, y, size, {0, 0, 0, 255}); // Obramowanie
    
    // Ikona jeśli dostępna
    if (stationIconTexture) {
        drawIcon(stationIconTexture, x, y, size * 2);
    }
}

void MapRenderer::renderTrains() {
    if (!gameState) return;
    
    const auto& trains = gameState->getAllTrains();
    
    for (const auto& train : trains) {
        if (train->getStatus() == TrainStatus::IN_SERVICE ||
            train->getStatus() == TrainStatus::WAITING) {
            
            if (isInView(train->getCurrentLatitude(), train->getCurrentLongitude())) {
                renderTrain(train.get());
            }
        }
    }
}

void MapRenderer::renderTrain(const Train* train) {
    if (!train) return;
    
    int x, y;
    camera->worldToScreen(train->getCurrentLatitude(), 
                         train->getCurrentLongitude(), x, y);
    
    // Kolor w zależności od stanu
    SDL_Color color;
    
    if (train->isDelayed()) {
        color = {255, 0, 0, 255}; // Czerwony - opóźniony
    } else if (train->getCurrentSpeed() > 0) {
        color = {0, 255, 0, 255}; // Zielony - w ruchu
    } else {
        color = {255, 255, 0, 255}; // Żółty - zatrzymany
    }
    
    // Rysuj pociąg
    SDL_Rect trainRect = {x - trainSize, y - trainSize, trainSize * 2, trainSize * 2};
    SDL_SetRenderDrawColor(renderer, color.r, color.g, color.b, color.a);
    SDL_RenderFillRect(renderer, &trainRect);
    
    // Obramowanie
    SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
    SDL_RenderDrawRect(renderer, &trainRect);
    
    // Kierunek ruchu (jeśli się porusza)
    if (train->getCurrentSpeed() > 0) {
        // TODO: Rysuj strzałkę kierunku
    }
}

void MapRenderer::renderSignals() {
    // TODO: Renderowanie sygnalizacji
}

void MapRenderer::renderLabels() {
    if (!showLabels || !labelFont) return;
    
    if (!gameState) return;
    
    // Etykiety stacji
    const auto& stations = gameState->getAllStations();
    
    for (const auto& station : stations) {
        if (isInView(station->getLatitude(), station->getLongitude())) {
            int x, y;
            camera->worldToScreen(station->getLatitude(), 
                                station->getLongitude(), x, y);
            
            // Rysuj tylko dla większych stacji przy małym zoomie
            if (camera->getZoom() < 0.5f && 
                station->getType() != StationType::MAJOR) {
                continue;
            }
            
            drawText(station->getName(), x + stationSize + 5, y - 5, textColor);
        }
    }
}

void MapRenderer::renderSelection() {
    SDL_SetRenderDrawColor(renderer, selectionColor.r, selectionColor.g, 
                          selectionColor.b, selectionColor.a);
    
    // Zaznaczona stacja
    if (!selectedStationId.empty()) {
        auto station = gameState->getStation(selectedStationId);
        if (station) {
            int x, y;
            camera->worldToScreen(station->getLatitude(), 
                                station->getLongitude(), x, y);
            
            // Rysuj pulsujące kółko
            int size = stationSize * 2 + 5 + 
                      sin(SDL_GetTicks() * 0.005) * 3;
            drawCircle(x, y, size, selectionColor);
            drawCircle(x, y, size + 1, selectionColor);
        }
    }
    
    // Zaznaczony pociąg
    if (!selectedTrainId.empty()) {
        auto train = gameState->getTrain(selectedTrainId);
        if (train) {
            int x, y;
            camera->worldToScreen(train->getCurrentLatitude(), 
                                train->getCurrentLongitude(), x, y);
            
            SDL_Rect selectRect = {
                x - trainSize - 5, 
                y - trainSize - 5, 
                (trainSize + 5) * 2, 
                (trainSize + 5) * 2
            };
            SDL_RenderDrawRect(renderer, &selectRect);
        }
    }
}

void MapRenderer::renderDebugInfo() {
    if (!debugFont) return;
    
    SDL_Color debugColor = {255, 255, 255, 255};
    
    // Info o kamerze
    std::string info = "Zoom: " + std::to_string(camera->getZoom()) +
                      " Center: " + std::to_string(camera->getCenterLat()) + 
                      ", " + std::to_string(camera->getCenterLon());
    
    drawText(info, 10, 10, debugColor);
    
    // FPS
    static int frameCount = 0;
    static Uint32 lastTime = SDL_GetTicks();
    frameCount++;
    
    Uint32 currentTime = SDL_GetTicks();
    if (currentTime - lastTime >= 1000) {
        float fps = frameCount * 1000.0f / (currentTime - lastTime);
        drawText("FPS: " + std::to_string(fps), 10, 30, debugColor);
        frameCount = 0;
        lastTime = currentTime;
    }
}

void MapRenderer::drawCircle(int x, int y, int radius, const SDL_Color& color) {
    SDL_SetRenderDrawColor(renderer, color.r, color.g, color.b, color.a);
    
    int dx = radius;
    int dy = 0;
    int radiusError = 1 - dx;
    
    while (dx >= dy) {
        SDL_RenderDrawPoint(renderer, x + dx, y + dy);
        SDL_RenderDrawPoint(renderer, x + dy, y + dx);
        SDL_RenderDrawPoint(renderer, x - dy, y + dx);
        SDL_RenderDrawPoint(renderer, x - dx, y + dy);
        SDL_RenderDrawPoint(renderer, x - dx, y - dy);
        SDL_RenderDrawPoint(renderer, x - dy, y - dx);
        SDL_RenderDrawPoint(renderer, x + dy, y - dx);
        SDL_RenderDrawPoint(renderer, x + dx, y - dy);
        
        dy++;
        if (radiusError < 0) {
            radiusError += 2 * dy + 1;
        } else {
            dx--;
            radiusError += 2 * (dy - dx) + 1;
        }
    }
}

void MapRenderer::drawFilledCircle(int x, int y, int radius, const SDL_Color& color) {
    SDL_SetRenderDrawColor(renderer, color.r, color.g, color.b, color.a);
    
    for (int w = 0; w < radius * 2; w++) {
        for (int h = 0; h < radius * 2; h++) {
            int dx = radius - w;
            int dy = radius - h;
            if ((dx*dx + dy*dy) <= (radius * radius)) {
                SDL_RenderDrawPoint(renderer, x + dx, y + dy);
            }
        }
    }
}

void MapRenderer::drawLine(int x1, int y1, int x2, int y2, 
                          const SDL_Color& color, int width) {
    SDL_SetRenderDrawColor(renderer, color.r, color.g, color.b, color.a);
    
    if (width == 1) {
        SDL_RenderDrawLine(renderer, x1, y1, x2, y2);
    } else {
        // Gruba linia
        float angle = atan2(y2 - y1, x2 - x1);
        float perpAngle = angle + M_PI / 2;
        
        for (int i = -width/2; i <= width/2; i++) {
            int offsetX = i * cos(perpAngle);
            int offsetY = i * sin(perpAngle);
            SDL_RenderDrawLine(renderer, x1 + offsetX, y1 + offsetY, 
                             x2 + offsetX, y2 + offsetY);
        }
    }
}

void MapRenderer::drawText(const std::string& text, int x, int y, 
                          const SDL_Color& color) {
    if (!labelFont) return;
    
    SDL_Surface* surface = TTF_RenderUTF8_Solid(labelFont, text.c_str(), color);
    if (!surface) return;
    
    SDL_Texture* texture = SDL_CreateTextureFromSurface(renderer, surface);
    if (texture) {
        SDL_Rect destRect = {x, y, surface->w, surface->h};
        SDL_RenderCopy(renderer, texture, nullptr, &destRect);
        SDL_DestroyTexture(texture);
    }
    
    SDL_FreeSurface(surface);
}

void MapRenderer::drawIcon(SDL_Texture* icon, int x, int y, int size) {
    if (!icon) return;
    
    SDL_Rect destRect = {x - size/2, y - size/2, size, size};
    SDL_RenderCopy(renderer, icon, nullptr, &destRect);
}

bool MapRenderer::loadTextures() {
    // Załaduj tekstury ikon
    stationIconTexture = loadTexture("assets/sprites/station_icon.png");
    trainIconTexture = loadTexture("assets/sprites/train_icon.png");
    
    // Załaduj tekstury terenu
    textures["grass"] = loadTexture("assets/sprites/terrain/grass.png");
    textures["water"] = loadTexture("assets/sprites/terrain/water.png");
    textures["forest"] = loadTexture("assets/sprites/terrain/forest.png");
    textures["city"] = loadTexture("assets/sprites/terrain/city.png");
    
    return true; // Nawet jeśli niektóre tekstury się nie załadują
}

bool MapRenderer::loadFonts() {
    labelFont = TTF_OpenFont("assets/fonts/arial.ttf", 12);
    if (!labelFont) {
        LOG_WARNING("Nie udało się załadować czcionki dla etykiet");
    }
    
    debugFont = TTF_OpenFont("assets/fonts/mono.ttf", 10);
    if (!debugFont) {
        LOG_WARNING("Nie udało się załadować czcionki debug");
    }
    
    return true;
}

SDL_Texture* MapRenderer::loadTexture(const std::string& path) {
    SDL_Surface* surface = IMG_Load(path.c_str());
    if (!surface) {
        LOG_WARNING("Nie udało się załadować obrazu: " + path);
        return nullptr;
    }
    
    SDL_Texture* texture = SDL_CreateTextureFromSurface(renderer, surface);
    SDL_FreeSurface(surface);
    
    if (!texture) {
        LOG_WARNING("Nie udało się utworzyć tekstury: " + path);
    }
    
    return texture;
}

MapBounds MapRenderer::calculateBounds() const {
    MapBounds bounds = {90.0, -90.0, 180.0, -180.0};
    
    if (!gameState) return bounds;
    
    // Oblicz na podstawie stacji
    const auto& stations = gameState->getAllStations();
    for (const auto& station : stations) {
        double lat = station->getLatitude();
        double lon = station->getLongitude();
        
        bounds.minLat = std::min(bounds.minLat, lat);
        bounds.maxLat = std::max(bounds.maxLat, lat);
        bounds.minLon = std::min(bounds.minLon, lon);
        bounds.maxLon = std::max(bounds.maxLon, lon);
    }
    
    // Dodaj margines
    double latMargin = (bounds.maxLat - bounds.minLat) * 0.1;
    double lonMargin = (bounds.maxLon - bounds.minLon) * 0.1;
    
    bounds.minLat -= latMargin;
    bounds.maxLat += latMargin;
    bounds.minLon -= lonMargin;
    bounds.maxLon += lonMargin;
    
    return bounds;
}

bool MapRenderer::isInView(double lat, double lon) const {
    return camera->isInView(lat, lon, 50);
}

double MapRenderer::distanceToLine(double px, double py, double x1, double y1, 
                                  double x2, double y2) const {
    double A = px - x1;
    double B = py - y1;
    double C = x2 - x1;
    double D = y2 - y1;
    
    double dot = A * C + B * D;
    double len_sq = C * C + D * D;
    double param = -1;
    
    if (len_sq != 0) {
        param = dot / len_sq;
    }
    
    double xx, yy;
    
    if (param < 0) {
        xx = x1;
        yy = y1;
    } else if (param > 1) {
        xx = x2;
        yy = y2;
    } else {
        xx = x1 + param * C;
        yy = y1 + param * D;
    }
    
    double dx = px - xx;
    double dy = py - yy;
    return sqrt(dx * dx + dy * dy);
}

void MapRenderer::setZoom(float zoom) {
    camera->setZoom(zoom);
}

float MapRenderer::getZoom() const {
    return camera->getZoom();
}

void MapRenderer::pan(int dx, int dy) {
    double lat, lon;
    camera->screenToWorld(camera->getViewportWidth()/2 - dx, 
                         camera->getViewportHeight()/2 - dy, lat, lon);
    camera->setCenter(lat, lon);
}

void MapRenderer::centerOn(double lat, double lon) {
    camera->setCenter(lat, lon);
}

void MapRenderer::resetView() {
    if (boundsCalculated) {
        double centerLat = (bounds.minLat + bounds.maxLat) / 2;
        double centerLon = (bounds.minLon + bounds.maxLon) / 2;
        camera->setCenter(centerLat, centerLon);
        camera->setZoom(1.0f);
    } else {
        // Domyślnie centrum Polski
        camera->setCenter(52.0, 19.0);
        camera->setZoom(1.0f);
    }
}

void MapRenderer::fitToBounds(const MapBounds& bounds) {
    double centerLat = (bounds.minLat + bounds.maxLat) / 2;
    double centerLon = (bounds.minLon + bounds.maxLon) / 2;
    camera->setCenter(centerLat, centerLon);
    
    // TODO: Oblicz odpowiedni zoom
}

void MapRenderer::setLayerVisible(const std::string& layer, bool visible) {
    if (layers.find(layer) != layers.end()) {
        layers[layer].visible = visible;
    }
}

bool MapRenderer::isLayerVisible(const std::string& layer) const {
    auto it = layers.find(layer);
    return (it != layers.end()) ? it->second.visible : false;
}

void MapRenderer::selectStation(const std::string& stationId) {
    selectedStationId = stationId;
    selectedTrainId.clear();
    selectedLineId.clear();
}

void MapRenderer::selectTrain(const std::string& trainId) {
    selectedTrainId = trainId;
    selectedStationId.clear();
    selectedLineId.clear();
}

void MapRenderer::selectLine(const std::string& lineId) {
    selectedLineId = lineId;
    selectedStationId.clear();
    selectedTrainId.clear();
}

void MapRenderer::clearSelection() {
    selectedStationId.clear();
    selectedTrainId.clear();
    selectedLineId.clear();
}

std::string MapRenderer::getStationAt(double worldX, double worldY) const {
    if (!gameState) return "";
    
    const auto& stations = gameState->getAllStations();
    
    for (const auto& station : stations) {
        int x, y;
        camera->worldToScreen(station->getLatitude(), 
                            station->getLongitude(), x, y);
        
        double dist = sqrt(pow(x - worldX, 2) + pow(y - worldY, 2));
        if (dist <= stationSize * 2) {
            return station->getId();
        }
    }
    
    return "";
}

std::string MapRenderer::getTrainAt(double worldX, double worldY) const {
    if (!gameState) return "";
    
    const auto& trains = gameState->getAllTrains();
    
    for (const auto& train : trains) {
        if (train->getStatus() == TrainStatus::IN_SERVICE ||
            train->getStatus() == TrainStatus::WAITING) {
            
            int x, y;
            camera->worldToScreen(train->getCurrentLatitude(), 
                                train->getCurrentLongitude(), x, y);
            
            if (abs(x - worldX) <= trainSize && abs(y - worldY) <= trainSize) {
                return train->getId();
            }
        }
    }
    
    return "";
}

std::string MapRenderer::getLineAt(double worldX, double worldY) const {
    if (!gameState) return "";
    
    const auto& lines = gameState->getAllLines();
    const double threshold = 5.0; // Piksele
    
    for (const auto& line : lines) {
        const auto& sections = line->getSections();
        
        for (const auto& section : sections) {
            auto fromStation = gameState->getStation(section.fromStationId);
            auto toStation = gameState->getStation(section.toStationId);
            
            if (fromStation && toStation) {
                int x1, y1, x2, y2;
                camera->worldToScreen(fromStation->getLatitude(), 
                                    fromStation->getLongitude(), x1, y1);
                camera->worldToScreen(toStation->getLatitude(), 
                                    toStation->getLongitude(), x2, y2);
                
                double dist = distanceToLine(worldX, worldY, x1, y1, x2, y2);
                if (dist <= threshold) {
                    return line->getId();
                }
            }
        }
    }
    
    return "";
}

int MapRenderer::worldToScreenX(double lon) const {
    int x, y;
    camera->worldToScreen(0, lon, x, y);
    return x;
}

int MapRenderer::worldToScreenY(double lat) const {
    int x, y;
    camera->worldToScreen(lat, 0, x, y);
    return y;
}

double MapRenderer::screenToWorldX(int x) const {
    double lat, lon;
    camera->screenToWorld(x, 0, lat, lon);
    return lon;
}

double MapRenderer::screenToWorldY(int y) const {
    double lat, lon;
    camera->screenToWorld(0, y, lat, lon);
    return lat;
}

// MapCamera implementation
MapCamera::MapCamera() {
}

void MapCamera::setViewport(int width, int height) {
    viewportWidth = width;
    viewportHeight = height;
}

void MapCamera::setCenter(double lat, double lon) {
    centerLat = lat;
    centerLon = lon;
}

void MapCamera::setZoom(float z) {
    zoom = std::max(0.1f, std::min(10.0f, z));
}

void MapCamera::pan(double dx, double dy) {
    // Konwertuj przesunięcie ekranu na przesunięcie w świecie
    double worldDx = dx / (zoom * 100.0);
    double worldDy = dy / (zoom * 100.0);
    
    centerLon += worldDx;
    centerLat -= worldDy;
}

void MapCamera::worldToScreen(double lat, double lon, int& x, int& y) const {
    // Prosta projekcja równokątna (Mercator dla małych obszarów)
    double scale = zoom * 100.0; // pikseli na stopień
    
    x = viewportWidth / 2 + (lon - centerLon) * scale;
    y = viewportHeight / 2 - (lat - centerLat) * scale;
}

void MapCamera::screenToWorld(int x, int y, double& lat, double& lon) const {
    double scale = zoom * 100.0;
    
    lon = centerLon + (x - viewportWidth / 2) / scale;
    lat = centerLat - (y - viewportHeight / 2) / scale;
}

bool MapCamera::isInView(double lat, double lon, int margin) const {
    int x, y;
    worldToScreen(lat, lon, x, y);
    
    return x >= -margin && x < viewportWidth + margin &&
           y >= -margin && y < viewportHeight + margin;
}

void MapCamera::getBounds(double& minLat, double& maxLat, 
                         double& minLon, double& maxLon) const {
    screenToWorld(0, viewportHeight, minLat, minLon);
    screenToWorld(viewportWidth, 0, maxLat, maxLon);
}

double MapCamera::metersPerPixel() const {
    // Przybliżenie dla środkowej szerokości geograficznej
    const double earthRadius = 6371000.0; // metry
    double latRad = centerLat * M_PI / 180.0;
    double metersPerDegree = earthRadius * M_PI / 180.0 * cos(latRad);
    return metersPerDegree / (zoom * 100.0);
}

double MapCamera::latToY(double lat) const {
    return viewportHeight / 2 - (lat - centerLat) * zoom * 100.0;
}

double MapCamera::lonToX(double lon) const {
    return viewportWidth / 2 + (lon - centerLon) * zoom * 100.0;
}

double MapCamera::yToLat(double y) const {
    return centerLat - (y - viewportHeight / 2) / (zoom * 100.0);
}

double MapCamera::xToLon(double x) const {
    return centerLon + (x - viewportWidth / 2) / (zoom * 100.0);
}