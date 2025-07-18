#ifndef MAINWINDOW_H
#define MAINWINDOW_H

#include <QMainWindow>
#include <memory>

QT_BEGIN_NAMESPACE
class QAction;
class QMenu;
class QToolBar;
class QStatusBar;
class QDockWidget;
class QTabWidget;
class QLabel;
class QPushButton;
class QTimer;
QT_END_NAMESPACE

// Forward declarations
class Game;
class Dashboard;
class MapWidget;
class TimetableEditor;
class FleetManager;
class FinancePanel;
class PersonnelPanel;

class MainWindow : public QMainWindow {
    Q_OBJECT
    
public:
    explicit MainWindow(std::shared_ptr<Game> game, QWidget *parent = nullptr);
    ~MainWindow();
    
protected:
    void closeEvent(QCloseEvent *event) override;
    
private slots:
    // Menu akcje
    void onNewGame();
    void onLoadGame();
    void onSaveGame();
    void onSaveGameAs();
    void onOptions();
    void onQuit();
    
    void onPauseResume();
    void onSpeedChange(int speed);
    
    void onShowDashboard();
    void onShowMap();
    void onShowTimetable();
    void onShowFleet();
    void onShowFinances();
    void onShowPersonnel();
    
    void onHelp();
    void onAbout();
    
    // Aktualizacje z gry
    void onGameStarted();
    void onGamePaused();
    void onGameResumed();
    void onGameStopped();
    void onDateChanged(int year, int month, int day);
    void onMoneyChanged(double amount);
    void onMessageReceived(const QString& message, const QString& type);
    
    // UI
    void updateStatusBar();
    void updateSpeedButtons();
    
private:
    void createActions();
    void createMenus();
    void createToolBars();
    void createStatusBar();
    void createDockWindows();
    void createCentralWidget();
    void connectSignals();
    
    void readSettings();
    void writeSettings();
    
    bool maybeSave();
    void setCurrentFile(const QString& fileName);
    
    // Gra
    std::shared_ptr<Game> game;
    
    // Główne widgety
    QTabWidget* centralTabs;
    Dashboard* dashboard;
    MapWidget* mapWidget;
    TimetableEditor* timetableEditor;
    FleetManager* fleetManager;
    FinancePanel* financePanel;
    PersonnelPanel* personnelPanel;
    
    // Docki
    QDockWidget* messageDock;
    QDockWidget* miniMapDock;
    
    // Menu
    QMenu* fileMenu;
    QMenu* gameMenu;
    QMenu* viewMenu;
    QMenu* helpMenu;
    
    // Toolbary
    QToolBar* fileToolBar;
    QToolBar* gameToolBar;
    QToolBar* viewToolBar;
    
    // Akcje
    QAction* newGameAct;
    QAction* loadGameAct;
    QAction* saveGameAct;
    QAction* saveAsAct;
    QAction* optionsAct;
    QAction* quitAct;
    
    QAction* pauseResumeAct;
    QAction* speed1xAct;
    QAction* speed2xAct;
    QAction* speed5xAct;
    QAction* speed10xAct;
    
    QAction* showDashboardAct;
    QAction* showMapAct;
    QAction* showTimetableAct;
    QAction* showFleetAct;
    QAction* showFinancesAct;
    QAction* showPersonnelAct;
    
    QAction* helpAct;
    QAction* aboutAct;
    QAction* aboutQtAct;
    
    // Status bar
    QLabel* dateLabel;
    QLabel* moneyLabel;
    QLabel* statusLabel;
    QLabel* fpsLabel;
    QPushButton* pauseButton;
    QPushButton* speedButton;
    
    // Stan
    QString currentFile;
    bool isModified = false;
    
    // Timery
    QTimer* statusUpdateTimer;
};

#endif // MAINWINDOW_H