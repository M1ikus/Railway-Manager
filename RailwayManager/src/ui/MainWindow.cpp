#include "MainWindow.h"
#include "Dashboard.h"
#include "MapWidget.h"
#include "TimetableEditor.h"
#include "FleetManager.h"
#include "FinancePanel.h"
#include "PersonnelPanel.h"
#include "core/Game.h"
#include "utils/Logger.h"

#include <QAction>
#include <QMenu>
#include <QMenuBar>
#include <QToolBar>
#include <QStatusBar>
#include <QDockWidget>
#include <QTabWidget>
#include <QLabel>
#include <QPushButton>
#include <QTimer>
#include <QMessageBox>
#include <QFileDialog>
#include <QSettings>
#include <QCloseEvent>
#include <QTextEdit>
#include <QListWidget>

MainWindow::MainWindow(std::shared_ptr<Game> game, QWidget *parent)
    : QMainWindow(parent), game(game) {
    
    setWindowTitle("Railway Manager");
    setWindowIcon(QIcon("assets/icons/app_icon.png"));
    
    // Utwórz UI
    createActions();
    createMenus();
    createToolBars();
    createStatusBar();
    createCentralWidget();
    createDockWindows();
    
    // Połącz sygnały
    connectSignals();
    
    // Wczytaj ustawienia
    readSettings();
    
    // Timer aktualizacji statusu
    statusUpdateTimer = new QTimer(this);
    connect(statusUpdateTimer, &QTimer::timeout, this, &MainWindow::updateStatusBar);
    statusUpdateTimer->start(100); // 10 FPS dla UI
    
    // Ustaw początkowy stan
    updateStatusBar();
    updateSpeedButtons();
}

MainWindow::~MainWindow() {
    writeSettings();
}

void MainWindow::createActions() {
    // Akcje menu Plik
    newGameAct = new QAction(QIcon("assets/icons/new_game.png"), tr("&Nowa gra"), this);
    newGameAct->setShortcut(QKeySequence::New);
    newGameAct->setStatusTip(tr("Rozpocznij nową grę"));
    connect(newGameAct, &QAction::triggered, this, &MainWindow::onNewGame);
    
    loadGameAct = new QAction(QIcon("assets/icons/load_game.png"), tr("&Wczytaj grę"), this);
    loadGameAct->setShortcut(QKeySequence::Open);
    loadGameAct->setStatusTip(tr("Wczytaj zapisaną grę"));
    connect(loadGameAct, &QAction::triggered, this, &MainWindow::onLoadGame);
    
    saveGameAct = new QAction(QIcon("assets/icons/save_game.png"), tr("&Zapisz grę"), this);
    saveGameAct->setShortcut(QKeySequence::Save);
    saveGameAct->setStatusTip(tr("Zapisz aktualną grę"));
    connect(saveGameAct, &QAction::triggered, this, &MainWindow::onSaveGame);
    
    saveAsAct = new QAction(tr("Zapisz &jako..."), this);
    saveAsAct->setShortcut(QKeySequence::SaveAs);
    saveAsAct->setStatusTip(tr("Zapisz grę pod nową nazwą"));
    connect(saveAsAct, &QAction::triggered, this, &MainWindow::onSaveGameAs);
    
    optionsAct = new QAction(QIcon("assets/icons/options.png"), tr("&Opcje"), this);
    optionsAct->setShortcut(tr("Ctrl+,"));
    optionsAct->setStatusTip(tr("Zmień ustawienia gry"));
    connect(optionsAct, &QAction::triggered, this, &MainWindow::onOptions);
    
    quitAct = new QAction(tr("&Zakończ"), this);
    quitAct->setShortcut(QKeySequence::Quit);
    quitAct->setStatusTip(tr("Zakończ grę"));
    connect(quitAct, &QAction::triggered, this, &MainWindow::onQuit);
    
    // Akcje menu Gra
    pauseResumeAct = new QAction(QIcon("assets/icons/pause.png"), tr("&Pauza"), this);
    pauseResumeAct->setShortcut(Qt::Key_Space);
    pauseResumeAct->setStatusTip(tr("Wstrzymaj/wznów grę"));
    pauseResumeAct->setCheckable(true);
    connect(pauseResumeAct, &QAction::triggered, this, &MainWindow::onPauseResume);
    
    speed1xAct = new QAction(tr("Prędkość &1x"), this);
    speed1xAct->setShortcut(Qt::Key_1);
    speed1xAct->setCheckable(true);
    speed1xAct->setChecked(true);
    connect(speed1xAct, &QAction::triggered, [this]() { onSpeedChange(1); });
    
    speed2xAct = new QAction(tr("Prędkość &2x"), this);
    speed2xAct->setShortcut(Qt::Key_2);
    speed2xAct->setCheckable(true);
    connect(speed2xAct, &QAction::triggered, [this]() { onSpeedChange(2); });
    
    speed5xAct = new QAction(tr("Prędkość &5x"), this);
    speed5xAct->setShortcut(Qt::Key_3);
    speed5xAct->setCheckable(true);
    connect(speed5xAct, &QAction::triggered, [this]() { onSpeedChange(5); });
    
    speed10xAct = new QAction(tr("Prędkość 1&0x"), this);
    speed10xAct->setShortcut(Qt::Key_4);
    speed10xAct->setCheckable(true);
    connect(speed10xAct, &QAction::triggered, [this]() { onSpeedChange(10); });
    
    // Grupa prędkości
    QActionGroup* speedGroup = new QActionGroup(this);
    speedGroup->addAction(speed1xAct);
    speedGroup->addAction(speed2xAct);
    speedGroup->addAction(speed5xAct);
    speedGroup->addAction(speed10xAct);
    
    // Akcje menu Widok
    showDashboardAct = new QAction(QIcon("assets/icons/dashboard.png"), tr("&Dashboard"), this);
    showDashboardAct->setShortcut(Qt::Key_F1);
    showDashboardAct->setStatusTip(tr("Pokaż dashboard"));
    connect(showDashboardAct, &QAction::triggered, this, &MainWindow::onShowDashboard);
    
    showMapAct = new QAction(QIcon("assets/icons/map.png"), tr("&Mapa"), this);
    showMapAct->setShortcut(Qt::Key_F2);
    showMapAct->setStatusTip(tr("Pokaż mapę"));
    connect(showMapAct, &QAction::triggered, this, &MainWindow::onShowMap);
    
    showTimetableAct = new QAction(QIcon("assets/icons/timetable.png"), tr("&Rozkład jazdy"), this);
    showTimetableAct->setShortcut(Qt::Key_F3);
    showTimetableAct->setStatusTip(tr("Zarządzaj rozkładami jazdy"));
    connect(showTimetableAct, &QAction::triggered, this, &MainWindow::onShowTimetable);
    
    showFleetAct = new QAction(QIcon("assets/icons/train.png"), tr("&Tabor"), this);
    showFleetAct->setShortcut(Qt::Key_F4);
    showFleetAct->setStatusTip(tr("Zarządzaj taborem"));
    connect(showFleetAct, &QAction::triggered, this, &MainWindow::onShowFleet);
    
    showFinancesAct = new QAction(QIcon("assets/icons/money.png"), tr("&Finanse"), this);
    showFinancesAct->setShortcut(Qt::Key_F5);
    showFinancesAct->setStatusTip(tr("Zarządzaj finansami"));
    connect(showFinancesAct, &QAction::triggered, this, &MainWindow::onShowFinances);
    
    showPersonnelAct = new QAction(QIcon("assets/icons/personnel.png"), tr("&Personel"), this);
    showPersonnelAct->setShortcut(Qt::Key_F6);
    showPersonnelAct->setStatusTip(tr("Zarządzaj personelem"));
    connect(showPersonnelAct, &QAction::triggered, this, &MainWindow::onShowPersonnel);
    
    // Akcje menu Pomoc
    helpAct = new QAction(QIcon("assets/icons/help.png"), tr("&Pomoc"), this);
    helpAct->setShortcut(QKeySequence::HelpContents);
    helpAct->setStatusTip(tr("Pokaż pomoc"));
    connect(helpAct, &QAction::triggered, this, &MainWindow::onHelp);
    
    aboutAct = new QAction(tr("&O programie"), this);
    aboutAct->setStatusTip(tr("Informacje o programie"));
    connect(aboutAct, &QAction::triggered, this, &MainWindow::onAbout);
    
    aboutQtAct = new QAction(tr("O &Qt"), this);
    aboutQtAct->setStatusTip(tr("Informacje o Qt"));
    connect(aboutQtAct, &QAction::triggered, qApp, &QApplication::aboutQt);
}

void MainWindow::createMenus() {
    // Menu Plik
    fileMenu = menuBar()->addMenu(tr("&Plik"));
    fileMenu->addAction(newGameAct);
    fileMenu->addAction(loadGameAct);
    fileMenu->addSeparator();
    fileMenu->addAction(saveGameAct);
    fileMenu->addAction(saveAsAct);
    fileMenu->addSeparator();
    fileMenu->addAction(optionsAct);
    fileMenu->addSeparator();
    fileMenu->addAction(quitAct);
    
    // Menu Gra
    gameMenu = menuBar()->addMenu(tr("&Gra"));
    gameMenu->addAction(pauseResumeAct);
    gameMenu->addSeparator();
    gameMenu->addAction(speed1xAct);
    gameMenu->addAction(speed2xAct);
    gameMenu->addAction(speed5xAct);
    gameMenu->addAction(speed10xAct);
    
    // Menu Widok
    viewMenu = menuBar()->addMenu(tr("&Widok"));
    viewMenu->addAction(showDashboardAct);
    viewMenu->addAction(showMapAct);
    viewMenu->addAction(showTimetableAct);
    viewMenu->addAction(showFleetAct);
    viewMenu->addAction(showFinancesAct);
    viewMenu->addAction(showPersonnelAct);
    
    // Menu Pomoc
    helpMenu = menuBar()->addMenu(tr("&Pomoc"));
    helpMenu->addAction(helpAct);
    helpMenu->addSeparator();
    helpMenu->addAction(aboutAct);
    helpMenu->addAction(aboutQtAct);
}

void MainWindow::createToolBars() {
    // Toolbar Plik
    fileToolBar = addToolBar(tr("Plik"));
    fileToolBar->addAction(newGameAct);
    fileToolBar->addAction(loadGameAct);
    fileToolBar->addAction(saveGameAct);
    
    // Toolbar Gra
    gameToolBar = addToolBar(tr("Gra"));
    gameToolBar->addAction(pauseResumeAct);
    gameToolBar->addSeparator();
    
    // Przyciski prędkości
    speedButton = new QPushButton("1x", this);
    speedButton->setMenu(new QMenu(this));
    speedButton->menu()->addAction(speed1xAct);
    speedButton->menu()->addAction(speed2xAct);
    speedButton->menu()->addAction(speed5xAct);
    speedButton->menu()->addAction(speed10xAct);
    gameToolBar->addWidget(speedButton);
    
    // Toolbar Widok
    viewToolBar = addToolBar(tr("Widok"));
    viewToolBar->addAction(showDashboardAct);
    viewToolBar->addAction(showMapAct);
    viewToolBar->addAction(showTimetableAct);
    viewToolBar->addAction(showFleetAct);
    viewToolBar->addAction(showFinancesAct);
    viewToolBar->addAction(showPersonnelAct);
}

void MainWindow::createStatusBar() {
    // Data
    dateLabel = new QLabel("1 stycznia 2024", this);
    dateLabel->setFrameStyle(QFrame::Panel | QFrame::Sunken);
    statusBar()->addWidget(dateLabel);
    
    // Pieniądze
    moneyLabel = new QLabel("0 PLN", this);
    moneyLabel->setFrameStyle(QFrame::Panel | QFrame::Sunken);
    statusBar()->addWidget(moneyLabel);
    
    // Status
    statusLabel = new QLabel("Gotowy", this);
    statusBar()->addWidget(statusLabel, 1);
    
    // FPS
    fpsLabel = new QLabel("0 FPS", this);
    fpsLabel->setFrameStyle(QFrame::Panel | QFrame::Sunken);
    statusBar()->addPermanentWidget(fpsLabel);
}

void MainWindow::createCentralWidget() {
    centralTabs = new QTabWidget(this);
    setCentralWidget(centralTabs);
    
    // Twórz główne panele (tymczasowo jako placeholdery)
    dashboard = new Dashboard(game.get(), this);
    mapWidget = new MapWidget(game.get(), this);
    timetableEditor = new TimetableEditor(game.get(), this);
    fleetManager = new FleetManager(game.get(), this);
    financePanel = new FinancePanel(game.get(), this);
    personnelPanel = new PersonnelPanel(game.get(), this);
    
    // Dodaj do zakładek
    centralTabs->addTab(dashboard, QIcon("assets/icons/dashboard.png"), tr("Dashboard"));
    centralTabs->addTab(mapWidget, QIcon("assets/icons/map.png"), tr("Mapa"));
    centralTabs->addTab(timetableEditor, QIcon("assets/icons/timetable.png"), tr("Rozkład jazdy"));
    centralTabs->addTab(fleetManager, QIcon("assets/icons/train.png"), tr("Tabor"));
    centralTabs->addTab(financePanel, QIcon("assets/icons/money.png"), tr("Finanse"));
    centralTabs->addTab(personnelPanel, QIcon("assets/icons/personnel.png"), tr("Personel"));
}

void MainWindow::createDockWindows() {
    // Dock wiadomości
    messageDock = new QDockWidget(tr("Wiadomości"), this);
    messageDock->setAllowedAreas(Qt::BottomDockWidgetArea | Qt::RightDockWidgetArea);
    
    QListWidget* messageList = new QListWidget(messageDock);
    messageDock->setWidget(messageList);
    
    addDockWidget(Qt::BottomDockWidgetArea, messageDock);
    viewMenu->addAction(messageDock->toggleViewAction());
    
    // Mini mapa
    miniMapDock = new QDockWidget(tr("Mini mapa"), this);
    miniMapDock->setAllowedAreas(Qt::LeftDockWidgetArea | Qt::RightDockWidgetArea);
    
    QLabel* miniMapLabel = new QLabel("Mini mapa", miniMapDock);
    miniMapLabel->setAlignment(Qt::AlignCenter);
    miniMapLabel->setMinimumSize(200, 200);
    miniMapDock->setWidget(miniMapLabel);
    
    addDockWidget(Qt::RightDockWidgetArea, miniMapDock);
    viewMenu->addAction(miniMapDock->toggleViewAction());
}

void MainWindow::connectSignals() {
    // Połącz sygnały z gry
    connect(game.get(), &Game::gameStarted, this, &MainWindow::onGameStarted);
    connect(game.get(), &Game::gamePaused, this, &MainWindow::onGamePaused);
    connect(game.get(), &Game::gameResumed, this, &MainWindow::onGameResumed);
    connect(game.get(), &Game::gameStopped, this, &MainWindow::onGameStopped);
    connect(game.get(), &Game::dateChanged, this, &MainWindow::onDateChanged);
    connect(game.get(), &Game::moneyChanged, this, &MainWindow::onMoneyChanged);
    connect(game.get(), &Game::messageReceived, this, &MainWindow::onMessageReceived);
}

void MainWindow::closeEvent(QCloseEvent *event) {
    if (maybeSave()) {
        writeSettings();
        event->accept();
    } else {
        event->ignore();
    }
}

void MainWindow::onNewGame() {
    if (maybeSave()) {
        // TODO: Dialog wyboru scenariusza
        game->newGame("default");
        setCurrentFile("");
        isModified = false;
    }
}

void MainWindow::onLoadGame() {
    if (maybeSave()) {
        QString fileName = QFileDialog::getOpenFileName(this,
            tr("Wczytaj grę"), "saves/", tr("Pliki zapisu (*.sav)"));
        
        if (!fileName.isEmpty()) {
            if (game->loadGame(fileName.toStdString())) {
                setCurrentFile(fileName);
                isModified = false;
            } else {
                QMessageBox::warning(this, tr("Błąd"),
                    tr("Nie udało się wczytać gry."));
            }
        }
    }
}

void MainWindow::onSaveGame() {
    if (currentFile.isEmpty()) {
        onSaveGameAs();
    } else {
        if (game->saveGame(currentFile.toStdString())) {
            isModified = false;
            statusBar()->showMessage(tr("Gra zapisana"), 2000);
        } else {
            QMessageBox::warning(this, tr("Błąd"),
                tr("Nie udało się zapisać gry."));
        }
    }
}

void MainWindow::onSaveGameAs() {
    QString fileName = QFileDialog::getSaveFileName(this,
        tr("Zapisz grę"), "saves/", tr("Pliki zapisu (*.sav)"));
    
    if (!fileName.isEmpty()) {
        if (game->saveGame(fileName.toStdString())) {
            setCurrentFile(fileName);
            isModified = false;
            statusBar()->showMessage(tr("Gra zapisana"), 2000);
        } else {
            QMessageBox::warning(this, tr("Błąd"),
                tr("Nie udało się zapisać gry."));
        }
    }
}

void MainWindow::onOptions() {
    // TODO: Dialog opcji
    QMessageBox::information(this, tr("Opcje"), 
        tr("Dialog opcji zostanie dodany wkrótce."));
}

void MainWindow::onQuit() {
    close();
}

void MainWindow::onPauseResume() {
    if (game->isPaused()) {
        game->startSimulation();
    } else {
        game->pauseSimulation();
    }
}

void MainWindow::onSpeedChange(int speed) {
    game->setSimulationSpeed(static_cast<float>(speed));
    speedButton->setText(QString::number(speed) + "x");
}

void MainWindow::onShowDashboard() {
    centralTabs->setCurrentWidget(dashboard);
}

void MainWindow::onShowMap() {
    centralTabs->setCurrentWidget(mapWidget);
}

void MainWindow::onShowTimetable() {
    centralTabs->setCurrentWidget(timetableEditor);
}

void MainWindow::onShowFleet() {
    centralTabs->setCurrentWidget(fleetManager);
}

void MainWindow::onShowFinances() {
    centralTabs->setCurrentWidget(financePanel);
}

void MainWindow::onShowPersonnel() {
    centralTabs->setCurrentWidget(personnelPanel);
}

void MainWindow::onHelp() {
    // TODO: System pomocy
    QMessageBox::information(this, tr("Pomoc"),
        tr("Railway Manager - Symulator zarządzania koleją\n\n"
           "Skróty klawiszowe:\n"
           "F1 - Dashboard\n"
           "F2 - Mapa\n"
           "F3 - Rozkład jazdy\n"
           "F4 - Tabor\n"
           "F5 - Finanse\n"
           "F6 - Personel\n"
           "Spacja - Pauza\n"
           "1-4 - Prędkość symulacji"));
}

void MainWindow::onAbout() {
    QMessageBox::about(this, tr("O Railway Manager"),
        tr("<h2>Railway Manager 1.0</h2>"
           "<p>Symulator zarządzania koleją</p>"
           "<p>Zarządzaj taborem, twórz rozkłady jazdy "
           "i rozwijaj swoją firmę kolejową!</p>"));
}

void MainWindow::onGameStarted() {
    pauseResumeAct->setEnabled(true);
    saveGameAct->setEnabled(true);
    saveAsAct->setEnabled(true);
    statusBar()->showMessage(tr("Gra rozpoczęta"), 2000);
}

void MainWindow::onGamePaused() {
    pauseResumeAct->setText(tr("&Wznów"));
    pauseResumeAct->setIcon(QIcon("assets/icons/play.png"));
    pauseResumeAct->setChecked(true);
    statusLabel->setText(tr("Pauza"));
}

void MainWindow::onGameResumed() {
    pauseResumeAct->setText(tr("&Pauza"));
    pauseResumeAct->setIcon(QIcon("assets/icons/pause.png"));
    pauseResumeAct->setChecked(false);
    statusLabel->setText(tr("Gra w toku"));
}

void MainWindow::onGameStopped() {
    pauseResumeAct->setEnabled(false);
    saveGameAct->setEnabled(false);
    saveAsAct->setEnabled(false);
    statusLabel->setText(tr("Gra zatrzymana"));
}

void MainWindow::onDateChanged(int year, int month, int day) {
    static const char* months[] = {
        "stycznia", "lutego", "marca", "kwietnia", "maja", "czerwca",
        "lipca", "sierpnia", "września", "października", "listopada", "grudnia"
    };
    
    QString dateStr = QString("%1 %2 %3")
        .arg(day)
        .arg(months[month - 1])
        .arg(year);
    
    dateLabel->setText(dateStr);
}

void MainWindow::onMoneyChanged(double amount) {
    QString moneyStr = QString("%L1 PLN").arg(amount, 0, 'f', 2);
    moneyLabel->setText(moneyStr);
    
    // Zmień kolor w zależności od salda
    if (amount < 0) {
        moneyLabel->setStyleSheet("QLabel { color: red; }");
    } else if (amount < 100000) {
        moneyLabel->setStyleSheet("QLabel { color: orange; }");
    } else {
        moneyLabel->setStyleSheet("QLabel { color: green; }");
    }
}

void MainWindow::onMessageReceived(const QString& message, const QString& type) {
    // Dodaj do docka wiadomości
    if (messageDock && messageDock->widget()) {
        QListWidget* list = qobject_cast<QListWidget*>(messageDock->widget());
        if (list) {
            QListWidgetItem* item = new QListWidgetItem(message);
            
            // Ustaw ikonę i kolor w zależności od typu
            if (type == "error") {
                item->setIcon(QIcon("assets/icons/error.png"));
                item->setForeground(Qt::red);
            } else if (type == "warning") {
                item->setIcon(QIcon("assets/icons/warning.png"));
                item->setForeground(QColor(255, 165, 0)); // Orange
            } else if (type == "info") {
                item->setIcon(QIcon("assets/icons/info.png"));
                item->setForeground(Qt::blue);
            } else {
                item->setIcon(QIcon("assets/icons/message.png"));
            }
            
            list->addItem(item);
            list->scrollToBottom();
            
            // Ogranicz liczbę wiadomości
            while (list->count() > 100) {
                delete list->takeItem(0);
            }
        }
    }
    
    // Pokaż także w status barze
    statusBar()->showMessage(message, 5000);
}

void MainWindow::updateStatusBar() {
    // Aktualizuj FPS
    static int frameCount = 0;
    static auto lastTime = std::chrono::steady_clock::now();
    
    frameCount++;
    auto currentTime = std::chrono::steady_clock::now();
    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(currentTime - lastTime).count();
    
    if (elapsed >= 1000) {
        float fps = frameCount * 1000.0f / elapsed;
        fpsLabel->setText(QString("%1 FPS").arg(fps, 0, 'f', 1));
        frameCount = 0;
        lastTime = currentTime;
    }
}

void MainWindow::updateSpeedButtons() {
    float speed = game->getSimulationSpeed();
    
    speed1xAct->setChecked(speed == 1.0f);
    speed2xAct->setChecked(speed == 2.0f);
    speed5xAct->setChecked(speed == 5.0f);
    speed10xAct->setChecked(speed == 10.0f);
    
    speedButton->setText(QString::number(static_cast<int>(speed)) + "x");
}

bool MainWindow::maybeSave() {
    if (!isModified) {
        return true;
    }
    
    QMessageBox::StandardButton ret = QMessageBox::warning(this, tr("Railway Manager"),
        tr("Gra została zmodyfikowana.\n"
           "Czy chcesz zapisać zmiany?"),
        QMessageBox::Save | QMessageBox::Discard | QMessageBox::Cancel);
    
    if (ret == QMessageBox::Save) {
        onSaveGame();
        return !isModified;
    } else if (ret == QMessageBox::Cancel) {
        return false;
    }
    
    return true;
}

void MainWindow::setCurrentFile(const QString& fileName) {
    currentFile = fileName;
    
    QString shownName = currentFile;
    if (currentFile.isEmpty()) {
        shownName = "nowa_gra.sav";
    }
    
    setWindowTitle(tr("%1[*] - Railway Manager").arg(QFileInfo(shownName).fileName()));
    setWindowModified(isModified);
}

void MainWindow::readSettings() {
    QSettings settings("RailwayManagerTeam", "RailwayManager");
    
    settings.beginGroup("MainWindow");
    resize(settings.value("size", QSize(1280, 800)).toSize());
    move(settings.value("pos", QPoint(200, 200)).toPoint());
    
    if (settings.value("maximized", false).toBool()) {
        showMaximized();
    }
    
    // Przywróć stan docków i toolbarów
    restoreState(settings.value("state").toByteArray());
    settings.endGroup();
}

void MainWindow::writeSettings() {
    QSettings settings("RailwayManagerTeam", "RailwayManager");
    
    settings.beginGroup("MainWindow");
    settings.setValue("size", size());
    settings.setValue("pos", pos());
    settings.setValue("maximized", isMaximized());
    settings.setValue("state", saveState());
    settings.endGroup();
}