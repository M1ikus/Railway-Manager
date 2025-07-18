#ifndef MAPRENDERER_H
#define MAPRENDERER_H

#include <SDL2/SDL.h>
#include <string>
#include <memory>
#include <unordered_map>
#include <vector>

// Forward declarations
class Game;
class GameState;
class Station;
class Train;
class Line;
class MapCamera;

struct MapLayer {
    bool visible = true;
    int zOrder = 0;
    SDL_Texture* texture = nullptr;
};

struct MapBounds {
    double minLat;
    double maxLat;
    double minLon;
    double maxLon;
};

class MapRenderer {
public:
    MapRenderer(Game* game);
    ~MapRenderer();
    
    // Inicjalizacja
    bool initialize(SDL_Renderer* renderer);
    void cleanup();
    
    // Renderowanie
    void render();
    void update();
    
    // Kontrola widoku
    void setZoom(float zoom);
    float getZoom() const;
    void pan(int dx, int dy);
    void centerOn(double lat, double lon);
    void resetView();
    void fitToBounds(const MapBounds& bounds);
    
    // Warstwy
    void setLayerVisible(const std::string& layer, bool visible);
    bool isLayerVisible(const std::string& layer) const;
    
    // Selekcja
    void selectStation(const std::string& stationId);
    void selectTrain(const std::string& trainId);
    void selectLine(const std::string& lineId);
    void clearSelection();
    
    // Detekcja kliknięć
    std::string getStationAt(double worldX, double worldY) const;
    std::string getTrainAt(double worldX, double worldY) const;
    std::string getLineAt(double worldX, double worldY) const;
    
    // Konwersja współrzędnych
    int worldToScreenX(double lon) const;
    int worldToScreenY(double lat) const;
    double screenToWorldX(int x) const;
    double screenToWorldY(int y) const;
    
    // Ustawienia renderowania
    void setShowGrid(bool show) { showGrid = show; }
    void setShowLabels(bool show) { showLabels = show; }
    void setMapType(int type) { mapType = type; }
    
private:
    // Renderowanie warstw
    void renderBackground();
    void renderTerrain();
    void renderWater();
    void renderGrid();
    void renderLines();
    void renderStations();
    void renderTrains();
    void renderSignals();
    void renderLabels();
    void renderSelection();
    void renderDebugInfo();
    
    // Renderowanie elementów
    void renderStation(const Station* station);
    void renderTrain(const Train* train);
    void renderLine(const Line* line);
    void renderLineSection(double lat1, double lon1, double lat2, double lon2, 
                          const SDL_Color& color, int width);
    
    // Pomocnicze
    void drawCircle(int x, int y, int radius, const SDL_Color& color);
    void drawFilledCircle(int x, int y, int radius, const SDL_Color& color);
    void drawLine(int x1, int y1, int x2, int y2, const SDL_Color& color, int width = 1);
    void drawText(const std::string& text, int x, int y, const SDL_Color& color);
    void drawIcon(SDL_Texture* icon, int x, int y, int size);
    
    // Ładowanie zasobów
    bool loadTextures();
    bool loadFonts();
    SDL_Texture* loadTexture(const std::string& path);
    
    // Obliczenia
    MapBounds calculateBounds() const;
    bool isInView(double lat, double lon) const;
    double distanceToLine(double px, double py, double x1, double y1, double x2, double y2) const;
    
    Game* game;
    GameState* gameState;
    SDL_Renderer* renderer;
    
    // Kamera
    std::unique_ptr<MapCamera> camera;
    
    // Warstwy
    std::unordered_map<std::string, MapLayer> layers;
    
    // Tekstury
    std::unordered_map<std::string, SDL_Texture*> textures;
    SDL_Texture* terrainTexture = nullptr;
    SDL_Texture* stationIconTexture = nullptr;
    SDL_Texture* trainIconTexture = nullptr;
    
    // Czcionka
    TTF_Font* labelFont = nullptr;
    TTF_Font* debugFont = nullptr;
    
    // Selekcja
    std::string selectedStationId;
    std::string selectedTrainId;
    std::string selectedLineId;
    
    // Ustawienia
    bool showGrid = false;
    bool showLabels = true;
    bool showDebugInfo = false;
    int mapType = 0; // 0=standard, 1=satellite, 2=schematic, 3=topographic
    
    // Cache
    MapBounds bounds;
    bool boundsCalculated = false;
    
    // Kolory
    SDL_Color backgroundColor = {240, 240, 240, 255};
    SDL_Color gridColor = {200, 200, 200, 128};
    SDL_Color selectionColor = {255, 255, 0, 255};
    SDL_Color textColor = {0, 0, 0, 255};
    
    // Rozmiary
    int stationSize = 10;
    int trainSize = 8;
    int lineWidth = 3;
    int selectionWidth = 3;
};

// Kamera mapy
class MapCamera {
public:
    MapCamera();
    
    void setViewport(int width, int height);
    void setCenter(double lat, double lon);
    void setZoom(float zoom);
    void pan(double dx, double dy);
    
    double getCenterLat() const { return centerLat; }
    double getCenterLon() const { return centerLon; }
    float getZoom() const { return zoom; }
    
    int getViewportWidth() const { return viewportWidth; }
    int getViewportHeight() const { return viewportHeight; }
    
    // Konwersja współrzędnych
    void worldToScreen(double lat, double lon, int& x, int& y) const;
    void screenToWorld(int x, int y, double& lat, double& lon) const;
    
    // Widoczność
    bool isInView(double lat, double lon, int margin = 0) const;
    void getBounds(double& minLat, double& maxLat, double& minLon, double& maxLon) const;
    
private:
    double centerLat = 52.0; // Centrum Polski
    double centerLon = 19.0;
    float zoom = 1.0f;
    
    int viewportWidth = 800;
    int viewportHeight = 600;
    
    // Parametry projekcji
    double metersPerPixel() const;
    double latToY(double lat) const;
    double lonToX(double lon) const;
    double yToLat(double y) const;
    double xToLon(double x) const;
};

#endif // MAPRENDERER_H