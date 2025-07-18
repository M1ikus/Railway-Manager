#ifndef DASHBOARD_H
#define DASHBOARD_H

#include <QWidget>
#include <memory>

QT_BEGIN_NAMESPACE
class QLabel;
class QPushButton;
class QProgressBar;
class QListWidget;
class QGroupBox;
class QVBoxLayout;
class QHBoxLayout;
class QGridLayout;
class QTimer;
QT_END_NAMESPACE

// Forward declarations
class Game;
class QuickStatsWidget;
class RecentEventsWidget;
class TrainStatusWidget;
class FinancialSummaryWidget;

class Dashboard : public QWidget {
    Q_OBJECT
    
public:
    explicit Dashboard(Game* game, QWidget* parent = nullptr);
    ~Dashboard();
    
public slots:
    void refresh();
    void updateStats();
    
private:
    void setupUI();
    void createQuickStats();
    void createRecentEvents();
    void createTrainStatus();
    void createFinancialSummary();
    void createQuickActions();
    
    Game* game;
    
    // Główne sekcje
    QuickStatsWidget* quickStats;
    RecentEventsWidget* recentEvents;
    TrainStatusWidget* trainStatus;
    FinancialSummaryWidget* financialSummary;
    
    // Timer odświeżania
    QTimer* refreshTimer;
};

// Widget szybkich statystyk
class QuickStatsWidget : public QGroupBox {
    Q_OBJECT
    
public:
    explicit QuickStatsWidget(Game* game, QWidget* parent = nullptr);
    
    void updateStats();
    
private:
    Game* game;
    
    QLabel* companyNameLabel;
    QLabel* moneyLabel;
    QLabel* reputationLabel;
    QLabel* dateLabel;
    
    QLabel* trainsLabel;
    QLabel* stationsLabel;
    QLabel* personnelLabel;
    QLabel* passengersLabel;
    
    QProgressBar* reputationBar;
};

// Widget ostatnich wydarzeń
class RecentEventsWidget : public QGroupBox {
    Q_OBJECT
    
public:
    explicit RecentEventsWidget(Game* game, QWidget* parent = nullptr);
    
    void addEvent(const QString& message, const QString& type);
    void refresh();
    
private:
    Game* game;
    QListWidget* eventsList;
};

// Widget statusu pociągów
class TrainStatusWidget : public QGroupBox {
    Q_OBJECT
    
public:
    explicit TrainStatusWidget(Game* game, QWidget* parent = nullptr);
    
    void updateStatus();
    
private:
    Game* game;
    
    QLabel* totalTrainsLabel;
    QLabel* activeTrainsLabel;
    QLabel* maintenanceTrainsLabel;
    QLabel* delayedTrainsLabel;
    
    QListWidget* criticalList;
};

// Widget podsumowania finansowego
class FinancialSummaryWidget : public QGroupBox {
    Q_OBJECT
    
public:
    explicit FinancialSummaryWidget(Game* game, QWidget* parent = nullptr);
    
    void updateFinances();
    
private:
    Game* game;
    
    QLabel* todayRevenueLabel;
    QLabel* todayExpensesLabel;
    QLabel* todayProfitLabel;
    
    QLabel* monthRevenueLabel;
    QLabel* monthExpensesLabel;
    QLabel* monthProfitLabel;
    
    QLabel* cashFlowLabel;
    QProgressBar* budgetBar;
};

#endif // DASHBOARD_H