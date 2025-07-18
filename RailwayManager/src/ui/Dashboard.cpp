#include "Dashboard.h"
#include "core/Game.h"
#include "core/GameState.h"
#include "models/Train.h"
#include "models/Station.h"
#include "utils/Logger.h"

#include <QLabel>
#include <QPushButton>
#include <QProgressBar>
#include <QListWidget>
#include <QGroupBox>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QTimer>
#include <QListWidgetItem>

Dashboard::Dashboard(Game* game, QWidget* parent)
    : QWidget(parent), game(game) {
    
    setupUI();
    
    // Timer odświeżania (co sekundę)
    refreshTimer = new QTimer(this);
    connect(refreshTimer, &QTimer::timeout, this, &Dashboard::refresh);
    refreshTimer->start(1000);
    
    // Pierwsze odświeżenie
    refresh();
}

Dashboard::~Dashboard() {
}

void Dashboard::setupUI() {
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    
    // Górny panel - statystyki i wydarzenia
    QHBoxLayout* topLayout = new QHBoxLayout();
    
    // Lewa strona - statystyki
    quickStats = new QuickStatsWidget(game, this);
    topLayout->addWidget(quickStats, 1);
    
    // Prawa strona - wydarzenia
    recentEvents = new RecentEventsWidget(game, this);
    topLayout->addWidget(recentEvents, 1);
    
    mainLayout->addLayout(topLayout);
    
    // Środkowy panel - status pociągów i finanse
    QHBoxLayout* middleLayout = new QHBoxLayout();
    
    trainStatus = new TrainStatusWidget(game, this);
    middleLayout->addWidget(trainStatus, 1);
    
    financialSummary = new FinancialSummaryWidget(game, this);
    middleLayout->addWidget(financialSummary, 1);
    
    mainLayout->addLayout(middleLayout);
    
    // Dolny panel - szybkie akcje
    createQuickActions();
}

void Dashboard::createQuickActions() {
    QGroupBox* actionsBox = new QGroupBox(tr("Szybkie akcje"), this);
    QHBoxLayout* actionsLayout = new QHBoxLayout(actionsBox);
    
    QPushButton* buyTrainBtn = new QPushButton(tr("Kup pociąg"), this);
    buyTrainBtn->setIcon(QIcon("assets/icons/train_add.png"));
    actionsLayout->addWidget(buyTrainBtn);
    
    QPushButton* hireStaffBtn = new QPushButton(tr("Zatrudnij personel"), this);
    hireStaffBtn->setIcon(QIcon("assets/icons/person_add.png"));
    actionsLayout->addWidget(hireStaffBtn);
    
    QPushButton* createTimetableBtn = new QPushButton(tr("Nowy rozkład"), this);
    createTimetableBtn->setIcon(QIcon("assets/icons/timetable_add.png"));
    actionsLayout->addWidget(createTimetableBtn);
    
    QPushButton* viewReportsBtn = new QPushButton(tr("Raporty"), this);
    viewReportsBtn->setIcon(QIcon("assets/icons/report.png"));
    actionsLayout->addWidget(viewReportsBtn);
    
    actionsLayout->addStretch();
    
    layout()->addWidget(actionsBox);
}

void Dashboard::refresh() {
    updateStats();
    quickStats->updateStats();
    recentEvents->refresh();
    trainStatus->updateStatus();
    financialSummary->updateFinances();
}

void Dashboard::updateStats() {
    // Aktualizacja głównych statystyk
    if (!game || !game->getGameState()) {
        return;
    }
}

// QuickStatsWidget
QuickStatsWidget::QuickStatsWidget(Game* game, QWidget* parent)
    : QGroupBox(tr("Przegląd"), parent), game(game) {
    
    QGridLayout* layout = new QGridLayout(this);
    
    // Nazwa firmy
    companyNameLabel = new QLabel(tr("Nowa Firma Kolejowa"), this);
    companyNameLabel->setStyleSheet("font-size: 18px; font-weight: bold;");
    layout->addWidget(companyNameLabel, 0, 0, 1, 2);
    
    // Data
    dateLabel = new QLabel(tr("1 stycznia 2024"), this);
    dateLabel->setStyleSheet("font-size: 14px;");
    layout->addWidget(dateLabel, 0, 2, 1, 2, Qt::AlignRight);
    
    // Pieniądze
    layout->addWidget(new QLabel(tr("Kapitał:"), this), 1, 0);
    moneyLabel = new QLabel("0 PLN", this);
    moneyLabel->setStyleSheet("font-size: 16px; font-weight: bold; color: green;");
    layout->addWidget(moneyLabel, 1, 1, 1, 3);
    
    // Reputacja
    layout->addWidget(new QLabel(tr("Reputacja:"), this), 2, 0);
    reputationBar = new QProgressBar(this);
    reputationBar->setRange(0, 100);
    reputationBar->setValue(50);
    reputationBar->setTextVisible(true);
    layout->addWidget(reputationBar, 2, 1, 1, 2);
    
    reputationLabel = new QLabel("50/100", this);
    layout->addWidget(reputationLabel, 2, 3);
    
    // Separator
    QFrame* line = new QFrame(this);
    line->setFrameShape(QFrame::HLine);
    line->setFrameShadow(QFrame::Sunken);
    layout->addWidget(line, 3, 0, 1, 4);
    
    // Statystyki
    layout->addWidget(new QLabel(tr("Pociągi:"), this), 4, 0);
    trainsLabel = new QLabel("0", this);
    layout->addWidget(trainsLabel, 4, 1);
    
    layout->addWidget(new QLabel(tr("Stacje:"), this), 4, 2);
    stationsLabel = new QLabel("0", this);
    layout->addWidget(stationsLabel, 4, 3);
    
    layout->addWidget(new QLabel(tr("Personel:"), this), 5, 0);
    personnelLabel = new QLabel("0", this);
    layout->addWidget(personnelLabel, 5, 1);
    
    layout->addWidget(new QLabel(tr("Pasażerowie dziś:"), this), 5, 2);
    passengersLabel = new QLabel("0", this);
    layout->addWidget(passengersLabel, 5, 3);
}

void QuickStatsWidget::updateStats() {
    if (!game || !game->getGameState()) {
        return;
    }
    
    GameState* state = game->getGameState();
    
    // Nazwa firmy
    companyNameLabel->setText(QString::fromStdString(state->getCompanyInfo().name));
    
    // Data
    auto date = state->getCurrentDate();
    static const char* months[] = {
        "stycznia", "lutego", "marca", "kwietnia", "maja", "czerwca",
        "lipca", "sierpnia", "września", "października", "listopada", "grudnia"
    };
    dateLabel->setText(QString("%1 %2 %3, %4:%5")
        .arg(date.day)
        .arg(months[date.month - 1])
        .arg(date.year)
        .arg(date.hour, 2, 10, QChar('0'))
        .arg(date.minute, 2, 10, QChar('0')));
    
    // Pieniądze
    double money = state->getMoney();
    moneyLabel->setText(QString("%L1 PLN").arg(money, 0, 'f', 2));
    if (money < 0) {
        moneyLabel->setStyleSheet("font-size: 16px; font-weight: bold; color: red;");
    } else if (money < 100000) {
        moneyLabel->setStyleSheet("font-size: 16px; font-weight: bold; color: orange;");
    } else {
        moneyLabel->setStyleSheet("font-size: 16px; font-weight: bold; color: green;");
    }
    
    // Reputacja
    int reputation = state->getCompanyInfo().reputation;
    reputationBar->setValue(reputation);
    reputationLabel->setText(QString("%1/100").arg(reputation));
    
    // Statystyki
    trainsLabel->setText(QString::number(state->getAllTrains().size()));
    stationsLabel->setText(QString::number(state->getAllStations().size()));
    personnelLabel->setText(QString::number(state->getAllPersonnel().size()));
    passengersLabel->setText(QString::number(state->getStatistics().totalPassengersTransported));
}

// RecentEventsWidget
RecentEventsWidget::RecentEventsWidget(Game* game, QWidget* parent)
    : QGroupBox(tr("Ostatnie wydarzenia"), parent), game(game) {
    
    QVBoxLayout* layout = new QVBoxLayout(this);
    
    eventsList = new QListWidget(this);
    eventsList->setAlternatingRowColors(true);
    layout->addWidget(eventsList);
    
    // Połącz z sygnałem wiadomości
    connect(game, &Game::messageReceived, this, &RecentEventsWidget::addEvent);
}

void RecentEventsWidget::addEvent(const QString& message, const QString& type) {
    QListWidgetItem* item = new QListWidgetItem(message);
    
    // Ustaw ikonę i kolor
    if (type == "error") {
        item->setIcon(QIcon("assets/icons/error.png"));
        item->setForeground(Qt::red);
    } else if (type == "warning") {
        item->setIcon(QIcon("assets/icons/warning.png"));
        item->setForeground(QColor(255, 165, 0));
    } else if (type == "info") {
        item->setIcon(QIcon("assets/icons/info.png"));
        item->setForeground(Qt::blue);
    } else if (type == "success") {
        item->setIcon(QIcon("assets/icons/success.png"));
        item->setForeground(Qt::darkGreen);
    }
    
    eventsList->insertItem(0, item);
    
    // Ogranicz liczbę wydarzeń
    while (eventsList->count() > 10) {
        delete eventsList->takeItem(eventsList->count() - 1);
    }
}

void RecentEventsWidget::refresh() {
    // Odświeżanie listy wydarzeń jeśli potrzebne
}

// TrainStatusWidget
TrainStatusWidget::TrainStatusWidget(Game* game, QWidget* parent)
    : QGroupBox(tr("Status taboru"), parent), game(game) {
    
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    
    // Statystyki
    QGridLayout* statsLayout = new QGridLayout();
    
    statsLayout->addWidget(new QLabel(tr("Wszystkie pociągi:"), this), 0, 0);
    totalTrainsLabel = new QLabel("0", this);
    statsLayout->addWidget(totalTrainsLabel, 0, 1);
    
    statsLayout->addWidget(new QLabel(tr("W trasie:"), this), 0, 2);
    activeTrainsLabel = new QLabel("0", this);
    activeTrainsLabel->setStyleSheet("color: green;");
    statsLayout->addWidget(activeTrainsLabel, 0, 3);
    
    statsLayout->addWidget(new QLabel(tr("W naprawie:"), this), 1, 0);
    maintenanceTrainsLabel = new QLabel("0", this);
    maintenanceTrainsLabel->setStyleSheet("color: orange;");
    statsLayout->addWidget(maintenanceTrainsLabel, 1, 1);
    
    statsLayout->addWidget(new QLabel(tr("Opóźnione:"), this), 1, 2);
    delayedTrainsLabel = new QLabel("0", this);
    delayedTrainsLabel->setStyleSheet("color: red;");
    statsLayout->addWidget(delayedTrainsLabel, 1, 3);
    
    mainLayout->addLayout(statsLayout);
    
    // Lista krytycznych
    mainLayout->addWidget(new QLabel(tr("Wymagają uwagi:"), this));
    
    criticalList = new QListWidget(this);
    criticalList->setMaximumHeight(100);
    mainLayout->addWidget(criticalList);
}

void TrainStatusWidget::updateStatus() {
    if (!game || !game->getGameState()) {
        return;
    }
    
    GameState* state = game->getGameState();
    const auto& trains = state->getAllTrains();
    
    int total = trains.size();
    int active = 0;
    int maintenance = 0;
    int delayed = 0;
    
    criticalList->clear();
    
    for (const auto& train : trains) {
        switch (train->getStatus()) {
            case TrainStatus::IN_SERVICE:
                active++;
                if (train->isDelayed()) {
                    delayed++;
                }
                break;
            case TrainStatus::MAINTENANCE:
                maintenance++;
                break;
            default:
                break;
        }
        
        // Sprawdź krytyczne
        if (train->getCondition() < 0.3f) {
            QListWidgetItem* item = new QListWidgetItem(
                QString("%1 - Zły stan techniczny (%2%)")
                    .arg(QString::fromStdString(train->getName()))
                    .arg(static_cast<int>(train->getCondition() * 100))
            );
            item->setIcon(QIcon("assets/icons/warning.png"));
            criticalList->addItem(item);
        }
        
        if (train->needsCleaning()) {
            QListWidgetItem* item = new QListWidgetItem(
                QString("%1 - Wymaga czyszczenia")
                    .arg(QString::fromStdString(train->getName()))
            );
            item->setIcon(QIcon("assets/icons/clean.png"));
            criticalList->addItem(item);
        }
        
        if (train->getDelay() > 30) {
            QListWidgetItem* item = new QListWidgetItem(
                QString("%1 - Duże opóźnienie (%2 min)")
                    .arg(QString::fromStdString(train->getName()))
                    .arg(train->getDelay())
            );
            item->setIcon(QIcon("assets/icons/delay.png"));
            criticalList->addItem(item);
        }
    }
    
    totalTrainsLabel->setText(QString::number(total));
    activeTrainsLabel->setText(QString::number(active));
    maintenanceTrainsLabel->setText(QString::number(maintenance));
    delayedTrainsLabel->setText(QString::number(delayed));
}

// FinancialSummaryWidget
FinancialSummaryWidget::FinancialSummaryWidget(Game* game, QWidget* parent)
    : QGroupBox(tr("Podsumowanie finansowe"), parent), game(game) {
    
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    
    // Dzisiejsze
    QGroupBox* todayBox = new QGroupBox(tr("Dzisiaj"), this);
    QGridLayout* todayLayout = new QGridLayout(todayBox);
    
    todayLayout->addWidget(new QLabel(tr("Przychody:"), this), 0, 0);
    todayRevenueLabel = new QLabel("0 PLN", this);
    todayRevenueLabel->setStyleSheet("color: green;");
    todayLayout->addWidget(todayRevenueLabel, 0, 1);
    
    todayLayout->addWidget(new QLabel(tr("Wydatki:"), this), 1, 0);
    todayExpensesLabel = new QLabel("0 PLN", this);
    todayExpensesLabel->setStyleSheet("color: red;");
    todayLayout->addWidget(todayExpensesLabel, 1, 1);
    
    todayLayout->addWidget(new QLabel(tr("Zysk:"), this), 2, 0);
    todayProfitLabel = new QLabel("0 PLN", this);
    todayProfitLabel->setStyleSheet("font-weight: bold;");
    todayLayout->addWidget(todayProfitLabel, 2, 1);
    
    mainLayout->addWidget(todayBox);
    
    // Miesięczne
    QGroupBox* monthBox = new QGroupBox(tr("Ten miesiąc"), this);
    QGridLayout* monthLayout = new QGridLayout(monthBox);
    
    monthLayout->addWidget(new QLabel(tr("Przychody:"), this), 0, 0);
    monthRevenueLabel = new QLabel("0 PLN", this);
    monthRevenueLabel->setStyleSheet("color: green;");
    monthLayout->addWidget(monthRevenueLabel, 0, 1);
    
    monthLayout->addWidget(new QLabel(tr("Wydatki:"), this), 1, 0);
    monthExpensesLabel = new QLabel("0 PLN", this);
    monthExpensesLabel->setStyleSheet("color: red;");
    monthLayout->addWidget(monthExpensesLabel, 1, 1);
    
    monthLayout->addWidget(new QLabel(tr("Zysk:"), this), 2, 0);
    monthProfitLabel = new QLabel("0 PLN", this);
    monthProfitLabel->setStyleSheet("font-weight: bold;");
    monthLayout->addWidget(monthProfitLabel, 2, 1);
    
    mainLayout->addWidget(monthBox);
    
    // Cash flow
    mainLayout->addWidget(new QLabel(tr("Przepływ gotówki:"), this));
    cashFlowLabel = new QLabel("+0 PLN/dzień", this);
    cashFlowLabel->setStyleSheet("font-size: 14px; font-weight: bold;");
    mainLayout->addWidget(cashFlowLabel);
    
    // Budżet
    budgetBar = new QProgressBar(this);
    budgetBar->setRange(0, 100);
    budgetBar->setValue(75);
    budgetBar->setFormat(tr("Wykorzystanie budżetu: %p%"));
    mainLayout->addWidget(budgetBar);
}

void FinancialSummaryWidget::updateFinances() {
    if (!game || !game->getGameState()) {
        return;
    }
    
    // TODO: Implementacja po dodaniu EconomyManager
    
    // Tymczasowe wartości
    todayRevenueLabel->setText("150,000 PLN");
    todayExpensesLabel->setText("80,000 PLN");
    todayProfitLabel->setText("70,000 PLN");
    todayProfitLabel->setStyleSheet("font-weight: bold; color: green;");
    
    monthRevenueLabel->setText("4,500,000 PLN");
    monthExpensesLabel->setText("3,200,000 PLN");
    monthProfitLabel->setText("1,300,000 PLN");
    monthProfitLabel->setStyleSheet("font-weight: bold; color: green;");
    
    cashFlowLabel->setText("+70,000 PLN/dzień");
    cashFlowLabel->setStyleSheet("font-size: 14px; font-weight: bold; color: green;");
}