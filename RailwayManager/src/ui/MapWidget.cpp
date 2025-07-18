#include "MapWidget.h"
#include "core/Game.h"
#include "core/GameState.h"
#include "map/MapRenderer.h"
#include "models/Station.h"
#include "models/Train.h"
#include "models/Line.h"
#include "utils/Logger.h"

#include <QTimer>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QSlider>
#include <QLabel>
#include <QComboBox>
#include <QPushButton>
#include <QCheckBox>
#include <QGroupBox>
#include <QLineEdit>
#include <QMouseEvent>
#include <QWheelEvent>
#include <QKeyEvent>
#include <QPainter>

// SDLRenderThread
SDLRenderThread::SDLRenderThread(WId windowId, QObject* parent)
    : QThread(parent), windowId(windowId) {
}

SDLRenderThread::~SDLRenderThread() {
    stop();
    wait();
}

void SDLRenderThread::stop() {
    running = false;
}

void SDLRenderThread::run() {
    // Utwórz okno SDL z natywnego uchwytu okna Qt
    sdlWindow = SDL_CreateWindowFrom(reinterpret_cast<void*>(windowId));
    if (!sdlWindow) {
        LOG_ERROR("Nie można utworzyć okna SDL: " + std::string(SDL_GetError()));
        return;
    }
    
    // Utwórz renderer
    sdlRenderer = SDL_CreateRenderer(sdlWindow, -1, 
        SDL_RENDERER_ACCELERATED | SDL_RENDERER_PRESENTVSYNC);
    if (!sdlRenderer) {
        LOG_ERROR("Nie można utworzyć renderera SDL: " + std::string(SDL_GetError()));
        SDL_DestroyWindow(sdlWindow);
        return;
    }
    
    // Główna pętla renderowania
    while (running) {
        if (renderer) {
            renderer->render();
        } else {
            // Domyślne czyszczenie ekranu
            SDL_SetRenderDrawColor(sdlRenderer, 240, 240, 240, 255);
            SDL_RenderClear(sdlRenderer);
            SDL_RenderPresent(sdlRenderer);
        }
        
        SDL_Delay(16); // ~60 FPS
    }
    
    // Cleanup
    if (sdlRenderer) {
        SDL_DestroyRenderer(sdlRenderer);
    }
    if (sdlWindow) {
        SDL_DestroyWindow(sdlWindow);
    }
}

// SDLWidget
SDLWidget::SDLWidget(QWidget* parent) : QWidget(parent) {
    setAttribute(Qt::WA_NativeWindow);
    setAttribute(Qt::WA_OpaquePaintEvent);
    setAttribute(Qt::WA_NoSystemBackground);
    setAttribute(Qt::WA_PaintOnScreen);
    
    setFocusPolicy(Qt::StrongFocus);
    setMinimumSize(640, 480);
}

SDLWidget::~SDLWidget() {
}

void SDLWidget::resizeEvent(QResizeEvent* event) {
    QWidget::resizeEvent(event);
    // SDL automatycznie obsługuje zmianę rozmiaru
}

void SDLWidget::paintEvent(QPaintEvent* event) {
    // Nic nie rób - SDL renderuje bezpośrednio
}

// MapWidget
MapWidget::MapWidget(Game* game, QWidget* parent)
    : QWidget(parent), game(game) {
    
    setupUI();
    connectSignals();
    
    // Utwórz renderer mapy
    mapRenderer = std::make_unique<MapRenderer>(game);
    
    // Uruchom wątek renderowania
    renderThread = new SDLRenderThread(sdlWidget->winId(), this);
    renderThread->setRenderer(mapRenderer.get());
    renderThread->start();
    
    // Timer aktualizacji
    updateTimer = new QTimer(this);
    connect(updateTimer, &QTimer::timeout, this, &MapWidget::updateMap);
    updateTimer->start(100); // 10 FPS dla aktualizacji pozycji
    
    LOG_INFO("MapWidget utworzony");
}

MapWidget::~MapWidget() {
    if (renderThread) {
        renderThread->stop();
        renderThread->wait();
    }
}

void MapWidget::setupUI() {
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    
    // Górny panel kontrolek
    createControls();
    
    // Główny obszar mapy
    QHBoxLayout* mapLayout = new QHBoxLayout();
    
    // Lewa strona - panel kontroli
    MapControlPanel* controlPanel = new MapControlPanel(this, this);
    connect(controlPanel, &MapControlPanel::zoomInClicked, this, &MapWidget::zoomIn);
    connect(controlPanel, &MapControlPanel::zoomOutClicked, this, &MapWidget::zoomOut);
    connect(controlPanel, &MapControlPanel::resetViewClicked, this, &MapWidget::resetView);
    mapLayout->addWidget(controlPanel);
    
    // Środek - mapa SDL
    sdlWidget = new SDLWidget(this);
    sdlWidget->setMinimumSize(800, 600);
    mapLayout->addWidget(sdlWidget, 1);
    
    // Prawa strona - legenda
    MapLegendPanel* legendPanel = new MapLegendPanel(this);
    mapLayout->addWidget(legendPanel);
    
    mainLayout->addLayout(mapLayout, 1);
    
    // Dolny panel informacyjny
    createInfoPanel();
}

void MapWidget::createControls() {
    QGroupBox* controlsBox = new QGroupBox(tr("Kontrola widoku"), this);
    QHBoxLayout* controlsLayout = new QHBoxLayout(controlsBox);
    
    // Zoom
    controlsLayout->addWidget(new QLabel(tr("Zoom:"), this));
    
    zoomSlider = new QSlider(Qt::Horizontal, this);
    zoomSlider->setRange(10, 200);
    zoomSlider->setValue(100);
    zoomSlider->setTickPosition(QSlider::TicksBelow);
    zoomSlider->setTickInterval(25);
    zoomSlider->setMinimumWidth(200);
    connect(zoomSlider, &QSlider::valueChanged, this, &MapWidget::onZoomChanged);
    controlsLayout->addWidget(zoomSlider);
    
    zoomLabel = new QLabel("100%", this);
    zoomLabel->setMinimumWidth(40);
    controlsLayout->addWidget(zoomLabel);
    
    controlsLayout->addSpacing(20);
    
    // Typ mapy
    controlsLayout->addWidget(new QLabel(tr("Typ mapy:"), this));
    
    mapTypeCombo = new QComboBox(this);
    mapTypeCombo->addItem(tr("Standardowa"));
    mapTypeCombo->addItem(tr("Satelitarna"));
    mapTypeCombo->addItem(tr("Schematyczna"));
    mapTypeCombo->addItem(tr("Topograficzna"));
    controlsLayout->addWidget(mapTypeCombo);
    
    controlsLayout->addSpacing(20);
    
    // Warstwy
    controlsLayout->addWidget(new QLabel(tr("Warstwy:"), this));
    
    showStationsCheck = new QCheckBox(tr("Stacje"), this);
    showStationsCheck->setChecked(true);
    connect(showStationsCheck, &QCheckBox::toggled, this, &MapWidget::onLayerToggled);
    controlsLayout->addWidget(showStationsCheck);
    
    showTrainsCheck = new QCheckBox(tr("Pociągi"), this);
    showTrainsCheck->setChecked(true);
    connect(showTrainsCheck, &QCheckBox::toggled, this, &MapWidget::onLayerToggled);
    controlsLayout->addWidget(showTrainsCheck);
    
    showLinesCheck = new QCheckBox(tr("Linie"), this);
    showLinesCheck->setChecked(true);
    connect(showLinesCheck, &QCheckBox::toggled, this, &MapWidget::onLayerToggled);
    controlsLayout->addWidget(showLinesCheck);
    
    showSignalsCheck = new QCheckBox(tr("Sygnały"), this);
    showSignalsCheck->setChecked(false);
    connect(showSignalsCheck, &QCheckBox::toggled, this, &MapWidget::onLayerToggled);
    controlsLayout->addWidget(showSignalsCheck);
    
    showLabelsCheck = new QCheckBox(tr("Etykiety"), this);
    showLabelsCheck->setChecked(true);
    connect(showLabelsCheck, &QCheckBox::toggled, this, &MapWidget::onLayerToggled);
    controlsLayout->addWidget(showLabelsCheck);
    
    showGridCheck = new QCheckBox(tr("Siatka"), this);
    showGridCheck->setChecked(false);
    connect(showGridCheck, &QCheckBox::toggled, this, &MapWidget::onLayerToggled);
    controlsLayout->addWidget(showGridCheck);
    
    controlsLayout->addStretch();
    
    layout()->addWidget(controlsBox);
}

void MapWidget::createInfoPanel() {
    QGroupBox* infoBox = new QGroupBox(tr("Informacje"), this);
    QHBoxLayout* infoLayout = new QHBoxLayout(infoBox);
    
    infoLabel = new QLabel(tr("Kliknij na mapę"), this);
    infoLayout->addWidget(infoLabel);
    
    infoLayout->addSpacing(20);
    
    coordsLabel = new QLabel(tr("Współrzędne: -"), this);
    infoLayout->addWidget(coordsLabel);
    
    infoLayout->addSpacing(20);
    
    selectedLabel = new QLabel(tr("Zaznaczone: brak"), this);
    infoLayout->addWidget(selectedLabel);
    
    infoLayout->addStretch();
    
    layout()->addWidget(infoBox);
}

void MapWidget::connectSignals() {
    // Połącz sygnały z gry
    if (game) {
        connect(game, &Game::simulationTick, this, &MapWidget::updateMap);
    }
}

void MapWidget::updateMap() {
    // Aktualizuj pozycje pociągów
    if (mapRenderer) {
        mapRenderer->update();
    }
}

void MapWidget::zoomIn() {
    int newZoom = std::min(zoomSlider->value() + 10, zoomSlider->maximum());
    zoomSlider->setValue(newZoom);
}

void MapWidget::zoomOut() {
    int newZoom = std::max(zoomSlider->value() - 10, zoomSlider->minimum());
    zoomSlider->setValue(newZoom);
}

void MapWidget::resetView() {
    zoomSlider->setValue(100);
    if (mapRenderer) {
        mapRenderer->resetView();
    }
}

void MapWidget::centerOnStation(const std::string& stationId) {
    if (!game || !game->getGameState()) return;
    
    auto station = game->getGameState()->getStation(stationId);
    if (station) {
        centerOnCoordinates(station->getLatitude(), station->getLongitude());
        selectStation(stationId);
    }
}

void MapWidget::centerOnTrain(const std::string& trainId) {
    if (!game || !game->getGameState()) return;
    
    auto train = game->getGameState()->getTrain(trainId);
    if (train) {
        centerOnCoordinates(train->getCurrentLatitude(), train->getCurrentLongitude());
        selectTrain(trainId);
    }
}

void MapWidget::centerOnCoordinates(double lat, double lon) {
    if (mapRenderer) {
        mapRenderer->centerOn(lat, lon);
    }
}

void MapWidget::setLayerVisible(const QString& layer, bool visible) {
    if (mapRenderer) {
        mapRenderer->setLayerVisible(layer.toStdString(), visible);
    }
}

bool MapWidget::isLayerVisible(const QString& layer) const {
    if (mapRenderer) {
        return mapRenderer->isLayerVisible(layer.toStdString());
    }
    return false;
}

void MapWidget::selectStation(const std::string& stationId) {
    selectedStationId = stationId;
    selectedTrainId.clear();
    selectedLineId.clear();
    
    if (mapRenderer) {
        mapRenderer->selectStation(stationId);
    }
    
    selectedLabel->setText(tr("Zaznaczone: Stacja ") + QString::fromStdString(stationId));
}

void MapWidget::selectTrain(const std::string& trainId) {
    selectedTrainId = trainId;
    selectedStationId.clear();
    selectedLineId.clear();
    
    if (mapRenderer) {
        mapRenderer->selectTrain(trainId);
    }
    
    selectedLabel->setText(tr("Zaznaczone: Pociąg ") + QString::fromStdString(trainId));
}

void MapWidget::selectLine(const std::string& lineId) {
    selectedLineId = lineId;
    selectedStationId.clear();
    selectedTrainId.clear();
    
    if (mapRenderer) {
        mapRenderer->selectLine(lineId);
    }
    
    selectedLabel->setText(tr("Zaznaczone: Linia ") + QString::fromStdString(lineId));
}

void MapWidget::clearSelection() {
    selectedStationId.clear();
    selectedTrainId.clear();
    selectedLineId.clear();
    
    if (mapRenderer) {
        mapRenderer->clearSelection();
    }
    
    selectedLabel->setText(tr("Zaznaczone: brak"));
}

void MapWidget::mousePressEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        isDragging = true;
        lastMousePos = event->pos();
        
        // Sprawdź co zostało kliknięte
        if (mapRenderer) {
            QPointF worldPos = mapToWorld(event->pos());
            
            // Sprawdź stacje
            std::string clickedStation = mapRenderer->getStationAt(worldPos.x(), worldPos.y());
            if (!clickedStation.empty()) {
                selectStation(clickedStation);
                emit stationClicked(QString::fromStdString(clickedStation));
                return;
            }
            
            // Sprawdź pociągi
            std::string clickedTrain = mapRenderer->getTrainAt(worldPos.x(), worldPos.y());
            if (!clickedTrain.empty()) {
                selectTrain(clickedTrain);
                emit trainClicked(QString::fromStdString(clickedTrain));
                return;
            }
            
            // Sprawdź linie
            std::string clickedLine = mapRenderer->getLineAt(worldPos.x(), worldPos.y());
            if (!clickedLine.empty()) {
                selectLine(clickedLine);
                emit lineClicked(QString::fromStdString(clickedLine));
                return;
            }
            
            // Nic nie kliknięte - wyczyść selekcję
            clearSelection();
            emit mapClicked(worldPos.x(), worldPos.y());
        }
    }
}

void MapWidget::mouseMoveEvent(QMouseEvent* event) {
    if (isDragging && mapRenderer) {
        QPoint delta = event->pos() - lastMousePos;
        mapRenderer->pan(delta.x(), delta.y());
        lastMousePos = event->pos();
    }
    
    // Aktualizuj współrzędne
    QPointF worldPos = mapToWorld(event->pos());
    coordsLabel->setText(QString("Współrzędne: %1, %2")
        .arg(worldPos.x(), 0, 'f', 6)
        .arg(worldPos.y(), 0, 'f', 6));
}

void MapWidget::mouseReleaseEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        isDragging = false;
    }
}

void MapWidget::wheelEvent(QWheelEvent* event) {
    int delta = event->angleDelta().y();
    if (delta > 0) {
        zoomIn();
    } else if (delta < 0) {
        zoomOut();
    }
}

void MapWidget::keyPressEvent(QKeyEvent* event) {
    switch (event->key()) {
        case Qt::Key_Plus:
        case Qt::Key_Equal:
            zoomIn();
            break;
        case Qt::Key_Minus:
            zoomOut();
            break;
        case Qt::Key_0:
            resetView();
            break;
        case Qt::Key_Escape:
            clearSelection();
            break;
        default:
            QWidget::keyPressEvent(event);
    }
}

void MapWidget::onZoomChanged(int value) {
    zoomLabel->setText(QString("%1%").arg(value));
    
    if (mapRenderer) {
        mapRenderer->setZoom(value / 100.0f);
    }
}

void MapWidget::onLayerToggled() {
    setLayerVisible("stations", showStationsCheck->isChecked());
    setLayerVisible("trains", showTrainsCheck->isChecked());
    setLayerVisible("lines", showLinesCheck->isChecked());
    setLayerVisible("signals", showSignalsCheck->isChecked());
    setLayerVisible("labels", showLabelsCheck->isChecked());
    setLayerVisible("grid", showGridCheck->isChecked());
}

void MapWidget::onFilterChanged() {
    // TODO: Implementacja filtrów
}

QPointF MapWidget::mapToWorld(const QPoint& screenPos) const {
    if (mapRenderer) {
        return QPointF(mapRenderer->screenToWorldX(screenPos.x()),
                      mapRenderer->screenToWorldY(screenPos.y()));
    }
    return QPointF();
}

QPoint MapWidget::worldToMap(double lat, double lon) const {
    if (mapRenderer) {
        return QPoint(mapRenderer->worldToScreenX(lon),
                     mapRenderer->worldToScreenY(lat));
    }
    return QPoint();
}

// MapControlPanel
MapControlPanel::MapControlPanel(MapWidget* mapWidget, QWidget* parent)
    : QWidget(parent), mapWidget(mapWidget) {
    
    QVBoxLayout* layout = new QVBoxLayout(this);
    
    // Tytuł
    QLabel* titleLabel = new QLabel(tr("Kontrola mapy"), this);
    titleLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    layout->addWidget(titleLabel);
    
    // Przyciski zoom
    QPushButton* zoomInBtn = new QPushButton(QIcon("assets/icons/zoom_in.png"), tr("Powiększ"), this);
    connect(zoomInBtn, &QPushButton::clicked, this, &MapControlPanel::zoomInClicked);
    layout->addWidget(zoomInBtn);
    
    QPushButton* zoomOutBtn = new QPushButton(QIcon("assets/icons/zoom_out.png"), tr("Pomniejsz"), this);
    connect(zoomOutBtn, &QPushButton::clicked, this, &MapControlPanel::zoomOutClicked);
    layout->addWidget(zoomOutBtn);
    
    QPushButton* resetBtn = new QPushButton(QIcon("assets/icons/reset.png"), tr("Resetuj widok"), this);
    connect(resetBtn, &QPushButton::clicked, this, &MapControlPanel::resetViewClicked);
    layout->addWidget(resetBtn);
    
    layout->addSpacing(20);
    
    // Wyszukiwarka
    QLabel* searchLabel = new QLabel(tr("Szukaj:"), this);
    layout->addWidget(searchLabel);
    
    QLineEdit* searchEdit = new QLineEdit(this);
    searchEdit->setPlaceholderText(tr("Nazwa stacji..."));
    layout->addWidget(searchEdit);
    
    QPushButton* searchBtn = new QPushButton(QIcon("assets/icons/search.png"), tr("Szukaj"), this);
    connect(searchBtn, &QPushButton::clicked, [this, searchEdit]() {
        emit searchRequested(searchEdit->text());
    });
    layout->addWidget(searchBtn);
    
    layout->addStretch();
    
    setMaximumWidth(200);
}

// MapLegendPanel
MapLegendPanel::MapLegendPanel(QWidget* parent) : QWidget(parent) {
    QVBoxLayout* layout = new QVBoxLayout(this);
    
    // Tytuł
    QLabel* titleLabel = new QLabel(tr("Legenda"), this);
    titleLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    layout->addWidget(titleLabel);
    
    // Elementy legendy
    createLegendItem(layout, QColor(255, 0, 0), tr("Stacja główna"));
    createLegendItem(layout, QColor(255, 128, 0), tr("Stacja regionalna"));
    createLegendItem(layout, QColor(255, 255, 0), tr("Stacja lokalna"));
    
    layout->addSpacing(10);
    
    createLegendItem(layout, QColor(0, 255, 0), tr("Pociąg w ruchu"));
    createLegendItem(layout, QColor(255, 0, 0), tr("Pociąg zatrzymany"));
    createLegendItem(layout, QColor(128, 128, 128), tr("Pociąg w naprawie"));
    
    layout->addSpacing(10);
    
    createLegendItem(layout, QColor(0, 0, 255), tr("Linia główna"));
    createLegendItem(layout, QColor(0, 128, 255), tr("Linia regionalna"));
    createLegendItem(layout, QColor(128, 128, 255), tr("Linia lokalna"));
    
    layout->addStretch();
    
    setMaximumWidth(200);
}

void MapLegendPanel::createLegendItem(QVBoxLayout* layout, const QColor& color, const QString& text) {
    QWidget* item = new QWidget(this);
    QHBoxLayout* itemLayout = new QHBoxLayout(item);
    itemLayout->setContentsMargins(0, 0, 0, 0);
    
    // Kolorowy kwadrat
    QLabel* colorLabel = new QLabel(this);
    colorLabel->setFixedSize(16, 16);
    colorLabel->setStyleSheet(QString("background-color: %1; border: 1px solid black;")
        .arg(color.name()));
    itemLayout->addWidget(colorLabel);
    
    // Tekst
    QLabel* textLabel = new QLabel(text, this);
    itemLayout->addWidget(textLabel);
    
    itemLayout->addStretch();
    
    layout->addWidget(item);
}