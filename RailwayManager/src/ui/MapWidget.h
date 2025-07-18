#ifndef MAPWIDGET_H
#define MAPWIDGET_H

#include <QWidget>
#include <QThread>
#include <memory>
#include <SDL2/SDL.h>

QT_BEGIN_NAMESPACE
class QTimer;
class QVBoxLayout;
class QHBoxLayout;
class QSlider;
class QLabel;
class QComboBox;
class QPushButton;
class QCheckBox;
QT_END_NAMESPACE

// Forward declarations
class Game;
class MapRenderer;
class Station;
class Train;
class Line;

// Thread dla renderowania SDL
class SDLRenderThread : public QThread {
    Q_OBJECT
    
public:
    SDLRenderThread(WId windowId, QObject* parent = nullptr);
    ~SDLRenderThread();
    
    void stop();
    void setRenderer(MapRenderer* renderer) { this->renderer = renderer; }
    
protected:
    void run() override;
    
private:
    WId windowId;
    SDL_Window* sdlWindow = nullptr;
    SDL_Renderer* sdlRenderer = nullptr;
    MapRenderer* renderer = nullptr;
    bool running = true;
};

// Widget SDL osadzony w Qt
class SDLWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit SDLWidget(QWidget* parent = nullptr);
    ~SDLWidget();
    
    SDL_Window* getSDLWindow() const { return sdlWindow; }
    SDL_Renderer* getSDLRenderer() const { return sdlRenderer; }
    
protected:
    void resizeEvent(QResizeEvent* event) override;
    void paintEvent(QPaintEvent* event) override;
    
private:
    SDL_Window* sdlWindow = nullptr;
    SDL_Renderer* sdlRenderer = nullptr;
};

// Główny widget mapy
class MapWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit MapWidget(Game* game, QWidget* parent = nullptr);
    ~MapWidget();
    
    // Kontrola widoku
    void zoomIn();
    void zoomOut();
    void resetView();
    void centerOnStation(const std::string& stationId);
    void centerOnTrain(const std::string& trainId);
    void centerOnCoordinates(double lat, double lon);
    
    // Warstwy
    void setLayerVisible(const QString& layer, bool visible);
    bool isLayerVisible(const QString& layer) const;
    
    // Selekcja
    void selectStation(const std::string& stationId);
    void selectTrain(const std::string& trainId);
    void selectLine(const std::string& lineId);
    void clearSelection();
    
signals:
    void stationClicked(const QString& stationId);
    void trainClicked(const QString& trainId);
    void lineClicked(const QString& lineId);
    void mapClicked(double lat, double lon);
    
protected:
    void mousePressEvent(QMouseEvent* event) override;
    void mouseMoveEvent(QMouseEvent* event) override;
    void mouseReleaseEvent(QMouseEvent* event) override;
    void wheelEvent(QWheelEvent* event) override;
    void keyPressEvent(QKeyEvent* event) override;
    
private slots:
    void updateMap();
    void onZoomChanged(int value);
    void onLayerToggled();
    void onFilterChanged();
    
private:
    void setupUI();
    void createControls();
    void createInfoPanel();
    void connectSignals();
    
    // Konwersja współrzędnych
    QPointF mapToWorld(const QPoint& screenPos) const;
    QPoint worldToMap(double lat, double lon) const;
    
    Game* game;
    
    // SDL
    SDLWidget* sdlWidget;
    SDLRenderThread* renderThread;
    std::unique_ptr<MapRenderer> mapRenderer;
    
    // Kontrolki
    QSlider* zoomSlider;
    QLabel* zoomLabel;
    QComboBox* mapTypeCombo;
    
    // Przyciski warstw
    QCheckBox* showStationsCheck;
    QCheckBox* showTrainsCheck;
    QCheckBox* showLinesCheck;
    QCheckBox* showSignalsCheck;
    QCheckBox* showLabelsCheck;
    QCheckBox* showGridCheck;
    
    // Panel informacyjny
    QLabel* infoLabel;
    QLabel* coordsLabel;
    QLabel* selectedLabel;
    
    // Stan
    bool isDragging = false;
    QPoint lastMousePos;
    
    // Selekcja
    std::string selectedStationId;
    std::string selectedTrainId;
    std::string selectedLineId;
    
    // Timer aktualizacji
    QTimer* updateTimer;
};

// Panel kontroli mapy
class MapControlPanel : public QWidget {
    Q_OBJECT
    
public:
    explicit MapControlPanel(MapWidget* mapWidget, QWidget* parent = nullptr);
    
signals:
    void zoomInClicked();
    void zoomOutClicked();
    void resetViewClicked();
    void searchRequested(const QString& query);
    
private:
    MapWidget* mapWidget;
};

// Panel legendy
class MapLegendPanel : public QWidget {
    Q_OBJECT
    
public:
    explicit MapLegendPanel(QWidget* parent = nullptr);
    
private:
    void createLegendItem(QVBoxLayout* layout, const QColor& color, const QString& text);
};

#endif // MAPWIDGET_H