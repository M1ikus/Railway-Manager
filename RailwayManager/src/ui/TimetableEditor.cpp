#include "TimetableEditor.h"
#include "core/Game.h"
#include "core/GameState.h"
#include "models/Train.h"
#include "models/Station.h"
#include "models/Line.h"
#include "models/Timetable.h"
#include "utils/Logger.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QSplitter>
#include <QTableWidget>
#include <QTreeWidget>
#include <QListWidget>
#include <QComboBox>
#include <QPushButton>
#include <QTimeEdit>
#include <QSpinBox>
#include <QCheckBox>
#include <QGroupBox>
#include <QLineEdit>
#include <QLabel>
#include <QMessageBox>
#include <QFileDialog>
#include <QHeaderView>
#include <QPainter>
#include <QTime>
#include <QDialog>
#include <QDialogButtonBox>

TimetableEditor::TimetableEditor(Game* game, QWidget* parent)
    : QWidget(parent), game(game) {
    
    setupUI();
    connectSignals();
    
    // Załaduj dane
    loadTimetables();
    populateTrains();
    populateLines();
    populateStations();
}

TimetableEditor::~TimetableEditor() {
}

void TimetableEditor::setupUI() {
    QVBoxLayout* mainLayout = new QVBoxLayout(this);
    
    // Główny splitter
    mainSplitter = new QSplitter(Qt::Horizontal, this);
    
    // Lewa strona - lista rozkładów
    createTimetableList();
    
    // Prawa strona - edytor
    QWidget* rightWidget = new QWidget(this);
    QVBoxLayout* rightLayout = new QVBoxLayout(rightWidget);
    
    createEditorPanel();
    createStopsEditor();
    createPreviewPanel();
    
    mainSplitter->addWidget(timetableTree);
    mainSplitter->addWidget(rightWidget);
    mainSplitter->setStretchFactor(0, 1);
    mainSplitter->setStretchFactor(1, 3);
    
    mainLayout->addWidget(mainSplitter);
}

void TimetableEditor::createTimetableList() {
    QWidget* listWidget = new QWidget(this);
    QVBoxLayout* listLayout = new QVBoxLayout(listWidget);
    
    // Nagłówek
    QLabel* listLabel = new QLabel(tr("Rozkłady jazdy"), this);
    listLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    listLayout->addWidget(listLabel);
    
    // Drzewo rozkładów
    timetableTree = new QTreeWidget(this);
    timetableTree->setHeaderLabels(QStringList() << tr("Nazwa") << tr("Pociąg") << tr("Status"));
    timetableTree->setRootIsDecorated(false);
    listLayout->addWidget(timetableTree);
    
    // Przyciski
    QHBoxLayout* buttonsLayout = new QHBoxLayout();
    
    newBtn = new QPushButton(QIcon("assets/icons/add.png"), tr("Nowy"), this);
    editBtn = new QPushButton(QIcon("assets/icons/edit.png"), tr("Edytuj"), this);
    deleteBtn = new QPushButton(QIcon("assets/icons/delete.png"), tr("Usuń"), this);
    duplicateBtn = new QPushButton(QIcon("assets/icons/copy.png"), tr("Duplikuj"), this);
    
    buttonsLayout->addWidget(newBtn);
    buttonsLayout->addWidget(editBtn);
    buttonsLayout->addWidget(deleteBtn);
    buttonsLayout->addWidget(duplicateBtn);
    
    listLayout->addLayout(buttonsLayout);
    
    // Import/Export
    QHBoxLayout* ioLayout = new QHBoxLayout();
    
    QPushButton* importBtn = new QPushButton(QIcon("assets/icons/import.png"), tr("Import"), this);
    QPushButton* exportBtn = new QPushButton(QIcon("assets/icons/export.png"), tr("Export"), this);
    
    ioLayout->addWidget(importBtn);
    ioLayout->addWidget(exportBtn);
    
    listLayout->addLayout(ioLayout);
    
    mainSplitter->addWidget(listWidget);
}

void TimetableEditor::createEditorPanel() {
    editorGroup = new QGroupBox(tr("Edytor rozkładu"), this);
    QGridLayout* editorLayout = new QGridLayout(editorGroup);
    
    // Nazwa
    editorLayout->addWidget(new QLabel(tr("Nazwa:"), this), 0, 0);
    nameEdit = new QLineEdit(this);
    editorLayout->addWidget(nameEdit, 0, 1, 1, 3);
    
    // Pociąg
    editorLayout->addWidget(new QLabel(tr("Pociąg:"), this), 1, 0);
    trainCombo = new QComboBox(this);
    editorLayout->addWidget(trainCombo, 1, 1);
    
    // Linia
    editorLayout->addWidget(new QLabel(tr("Linia:"), this), 1, 2);
    lineCombo = new QComboBox(this);
    editorLayout->addWidget(lineCombo, 1, 3);
    
    // Aktywny
    activeCheck = new QCheckBox(tr("Aktywny"), this);
    activeCheck->setChecked(true);
    editorLayout->addWidget(activeCheck, 2, 0, 1, 2);
    
    layout()->addWidget(editorGroup);
}

void TimetableEditor::createStopsEditor() {
    QGroupBox* stopsGroup = new QGroupBox(tr("Przystanki"), this);
    QVBoxLayout* stopsLayout = new QVBoxLayout(stopsGroup);
    
    // Tabela przystanków
    stopsTable = new QTableWidget(0, 5, this);
    stopsTable->setHorizontalHeaderLabels(QStringList() 
        << tr("Stacja") << tr("Przyjazd") << tr("Odjazd") 
        << tr("Peron") << tr("Opcjonalny"));
    stopsTable->horizontalHeader()->setStretchLastSection(true);
    stopsTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    stopsLayout->addWidget(stopsTable);
    
    // Kontrolki dodawania
    QGroupBox* addGroup = new QGroupBox(tr("Dodaj przystanek"), this);
    QGridLayout* addLayout = new QGridLayout(addGroup);
    
    addLayout->addWidget(new QLabel(tr("Stacja:"), this), 0, 0);
    stationCombo = new QComboBox(this);
    addLayout->addWidget(stationCombo, 0, 1, 1, 3);
    
    addLayout->addWidget(new QLabel(tr("Przyjazd:"), this), 1, 0);
    arrivalEdit = new QTimeEdit(this);
    arrivalEdit->setDisplayFormat("HH:mm");
    addLayout->addWidget(arrivalEdit, 1, 1);
    
    addLayout->addWidget(new QLabel(tr("Odjazd:"), this), 1, 2);
    departureEdit = new QTimeEdit(this);
    departureEdit->setDisplayFormat("HH:mm");
    addLayout->addWidget(departureEdit, 1, 3);
    
    addLayout->addWidget(new QLabel(tr("Peron:"), this), 2, 0);
    platformSpin = new QSpinBox(this);
    platformSpin->setRange(1, 20);
    addLayout->addWidget(platformSpin, 2, 1);
    
    optionalCheck = new QCheckBox(tr("Przystanek opcjonalny"), this);
    addLayout->addWidget(optionalCheck, 2, 2, 1, 2);
    
    stopsLayout->addWidget(addGroup);
    
    // Przyciski
    QHBoxLayout* stopsButtons = new QHBoxLayout();
    
    addStopBtn = new QPushButton(QIcon("assets/icons/add.png"), tr("Dodaj"), this);
    removeStopBtn = new QPushButton(QIcon("assets/icons/remove.png"), tr("Usuń"), this);
    moveUpBtn = new QPushButton(QIcon("assets/icons/up.png"), tr("W górę"), this);
    moveDownBtn = new QPushButton(QIcon("assets/icons/down.png"), tr("W dół"), this);
    
    stopsButtons->addWidget(addStopBtn);
    stopsButtons->addWidget(removeStopBtn);
    stopsButtons->addWidget(moveUpBtn);
    stopsButtons->addWidget(moveDownBtn);
    stopsButtons->addStretch();
    
    stopsLayout->addLayout(stopsButtons);
    
    // Częstotliwość
    frequencyGroup = new QGroupBox(tr("Częstotliwość kursowania"), this);
    QGridLayout* freqLayout = new QGridLayout(frequencyGroup);
    
    freqLayout->addWidget(new QLabel(tr("Co ile minut:"), this), 0, 0);
    frequencySpin = new QSpinBox(this);
    frequencySpin->setRange(0, 120);
    frequencySpin->setSuffix(" min");
    frequencySpin->setSpecialValueText(tr("Pojedynczy kurs"));
    freqLayout->addWidget(frequencySpin, 0, 1);
    
    freqLayout->addWidget(new QLabel(tr("Pierwszy odjazd:"), this), 1, 0);
    firstDepartureEdit = new QTimeEdit(this);
    firstDepartureEdit->setDisplayFormat("HH:mm");
    firstDepartureEdit->setTime(QTime(5, 0));
    freqLayout->addWidget(firstDepartureEdit, 1, 1);
    
    freqLayout->addWidget(new QLabel(tr("Ostatni odjazd:"), this), 1, 2);
    lastDepartureEdit = new QTimeEdit(this);
    lastDepartureEdit->setDisplayFormat("HH:mm");
    lastDepartureEdit->setTime(QTime(23, 0));
    freqLayout->addWidget(lastDepartureEdit, 1, 3);
    
    stopsLayout->addWidget(frequencyGroup);
    
    layout()->addWidget(stopsGroup);
}

void TimetableEditor::createPreviewPanel() {
    QGroupBox* previewGroup = new QGroupBox(tr("Podgląd"), this);
    QVBoxLayout* previewLayout = new QVBoxLayout(previewGroup);
    
    // Tabela podglądu
    previewTable = new QTableWidget(0, 4, this);
    previewTable->setHorizontalHeaderLabels(QStringList() 
        << tr("Godzina") << tr("Stacja") << tr("Peron") << tr("Czas podróży"));
    previewTable->horizontalHeader()->setStretchLastSection(true);
    previewLayout->addWidget(previewTable);
    
    // Przyciski
    QHBoxLayout* previewButtons = new QHBoxLayout();
    
    calculateBtn = new QPushButton(QIcon("assets/icons/calculate.png"), 
                                  tr("Oblicz czasy"), this);
    validateBtn = new QPushButton(QIcon("assets/icons/check.png"), 
                                 tr("Sprawdź"), this);
    applyBtn = new QPushButton(QIcon("assets/icons/save.png"), 
                              tr("Zastosuj"), this);
    
    previewButtons->addWidget(calculateBtn);
    previewButtons->addWidget(validateBtn);
    previewButtons->addStretch();
    previewButtons->addWidget(applyBtn);
    
    previewLayout->addLayout(previewButtons);
    
    layout()->addWidget(previewGroup);
}

void TimetableEditor::connectSignals() {
    // Lista rozkładów
    connect(timetableTree, &QTreeWidget::currentItemChanged, 
            this, &TimetableEditor::onTimetableSelected);
    
    // Przyciski
    connect(newBtn, &QPushButton::clicked, this, &TimetableEditor::onNewTimetable);
    connect(editBtn, &QPushButton::clicked, this, &TimetableEditor::onEditTimetable);
    connect(deleteBtn, &QPushButton::clicked, this, &TimetableEditor::onDeleteTimetable);
    connect(duplicateBtn, &QPushButton::clicked, this, &TimetableEditor::onDuplicateTimetable);
    
    // Edytor
    connect(trainCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &TimetableEditor::onTrainChanged);
    connect(lineCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &TimetableEditor::onLineChanged);
    
    // Przystanki
    connect(addStopBtn, &QPushButton::clicked, this, &TimetableEditor::onStopAdded);
    connect(removeStopBtn, &QPushButton::clicked, this, &TimetableEditor::onStopRemoved);
    connect(moveUpBtn, &QPushButton::clicked, [this]() { onStopMoved(-1); });
    connect(moveDownBtn, &QPushButton::clicked, [this]() { onStopMoved(1); });
    
    // Podgląd
    connect(calculateBtn, &QPushButton::clicked, this, &TimetableEditor::onCalculateTimes);
    connect(validateBtn, &QPushButton::clicked, this, &TimetableEditor::onValidateTimetable);
    connect(applyBtn, &QPushButton::clicked, this, &TimetableEditor::onApplyTimetable);
}

void TimetableEditor::loadTimetables() {
    timetableTree->clear();
    
    if (!game || !game->getGameState()) return;
    
    const auto& timetables = game->getGameState()->getAllTimetables();
    
    for (const auto& timetable : timetables) {
        QTreeWidgetItem* item = new QTreeWidgetItem();
        item->setText(0, QString::fromStdString(timetable->getName()));
        item->setText(1, QString::fromStdString(timetable->getTrainId()));
        item->setText(2, timetable->isActive() ? tr("Aktywny") : tr("Nieaktywny"));
        item->setData(0, Qt::UserRole, QString::fromStdString(timetable->getId()));
        
        if (!timetable->isActive()) {
            item->setForeground(2, Qt::red);
        }
        
        timetableTree->addTopLevelItem(item);
    }
}

void TimetableEditor::populateTrains() {
    trainCombo->clear();
    trainCombo->addItem(tr("-- Wybierz pociąg --"));
    
    if (!game || !game->getGameState()) return;
    
    const auto& trains = game->getGameState()->getAllTrains();
    
    for (const auto& train : trains) {
        if (train->isAvailable()) {
            trainCombo->addItem(
                QString::fromStdString(train->getName()),
                QString::fromStdString(train->getId())
            );
        }
    }
}

void TimetableEditor::populateLines() {
    lineCombo->clear();
    lineCombo->addItem(tr("-- Wybierz linię --"));
    
    if (!game || !game->getGameState()) return;
    
    const auto& lines = game->getGameState()->getAllLines();
    
    for (const auto& line : lines) {
        lineCombo->addItem(
            QString::fromStdString(line->getName()),
            QString::fromStdString(line->getId())
        );
    }
}

void TimetableEditor::populateStations() {
    stationCombo->clear();
    stationCombo->addItem(tr("-- Wybierz stację --"));
    
    if (!game || !game->getGameState()) return;
    
    const auto& stations = game->getGameState()->getAllStations();
    
    for (const auto& station : stations) {
        stationCombo->addItem(
            QString::fromStdString(station->getName()),
            QString::fromStdString(station->getId())
        );
    }
}

void TimetableEditor::onNewTimetable() {
    NewTimetableDialog dialog(game, this);
    
    if (dialog.exec() == QDialog::Accepted) {
        // Utwórz nowy rozkład
        currentTimetable = TimetableEntry();
        currentTimetable.id = "timetable_" + std::to_string(QDateTime::currentMSecsSinceEpoch());
        currentTimetable.name = dialog.getName().toStdString();
        currentTimetable.trainId = dialog.getTrainId().toStdString();
        currentTimetable.lineId = dialog.getLineId().toStdString();
        currentTimetable.isActive = true;
        
        // Ustaw w UI
        nameEdit->setText(dialog.getName());
        
        for (int i = 0; i < trainCombo->count(); ++i) {
            if (trainCombo->itemData(i).toString() == dialog.getTrainId()) {
                trainCombo->setCurrentIndex(i);
                break;
            }
        }
        
        for (int i = 0; i < lineCombo->count(); ++i) {
            if (lineCombo->itemData(i).toString() == dialog.getLineId()) {
                lineCombo->setCurrentIndex(i);
                break;
            }
        }
        
        activeCheck->setChecked(true);
        
        // Wyczyść przystanki
        currentTimetable.stops.clear();
        updateStopsList();
        
        isModified = true;
    }
}

void TimetableEditor::onEditTimetable() {
    // Edycja wybranego rozkładu
    onTimetableSelected();
}

void TimetableEditor::onDeleteTimetable() {
    QTreeWidgetItem* current = timetableTree->currentItem();
    if (!current) return;
    
    QString timetableId = current->data(0, Qt::UserRole).toString();
    QString name = current->text(0);
    
    QMessageBox::StandardButton reply = QMessageBox::question(this,
        tr("Usuń rozkład"),
        tr("Czy na pewno chcesz usunąć rozkład '%1'?").arg(name),
        QMessageBox::Yes | QMessageBox::No);
    
    if (reply == QMessageBox::Yes) {
        // TODO: Usuń z GameState
        emit timetableDeleted(timetableId);
        delete current;
    }
}

void TimetableEditor::onDuplicateTimetable() {
    QTreeWidgetItem* current = timetableTree->currentItem();
    if (!current) return;
    
    // Załaduj wybrany rozkład
    QString timetableId = current->data(0, Qt::UserRole).toString();
    loadTimetable(timetableId.toStdString());
    
    // Zmień ID i nazwę
    currentTimetable.id = "timetable_" + std::to_string(QDateTime::currentMSecsSinceEpoch());
    currentTimetable.name += " (kopia)";
    nameEdit->setText(QString::fromStdString(currentTimetable.name));
    
    isModified = true;
}

void TimetableEditor::onImportTimetable() {
    QString fileName = QFileDialog::getOpenFileName(this,
        tr("Importuj rozkład"), "", tr("Pliki CSV (*.csv);;Pliki TXT (*.txt)"));
    
    if (!fileName.isEmpty()) {
        // TODO: Implementacja importu
        QMessageBox::information(this, tr("Import"),
            tr("Import rozkładów zostanie zaimplementowany wkrótce."));
    }
}

void TimetableEditor::onExportTimetable() {
    QTreeWidgetItem* current = timetableTree->currentItem();
    if (!current) return;
    
    QString fileName = QFileDialog::getSaveFileName(this,
        tr("Eksportuj rozkład"), "", tr("Pliki CSV (*.csv);;Pliki TXT (*.txt)"));
    
    if (!fileName.isEmpty()) {
        // TODO: Implementacja eksportu
        QMessageBox::information(this, tr("Eksport"),
            tr("Eksport rozkładów zostanie zaimplementowany wkrótce."));
    }
}

void TimetableEditor::onTimetableSelected() {
    QTreeWidgetItem* current = timetableTree->currentItem();
    if (!current) return;
    
    QString timetableId = current->data(0, Qt::UserRole).toString();
    loadTimetable(timetableId.toStdString());
}

void TimetableEditor::loadTimetable(const std::string& timetableId) {
    if (!game || !game->getGameState()) return;
    
    auto timetable = game->getGameState()->getTimetable(timetableId);
    if (!timetable) return;
    
    // Załaduj dane do struktury
    currentTimetable.id = timetable->getId();
    currentTimetable.name = timetable->getName();
    currentTimetable.trainId = timetable->getTrainId();
    currentTimetable.lineId = timetable->getLineId();
    currentTimetable.isActive = timetable->isActive();
    
    // Ustaw w UI
    nameEdit->setText(QString::fromStdString(currentTimetable.name));
    activeCheck->setChecked(currentTimetable.isActive);
    
    // Wybierz pociąg
    for (int i = 0; i < trainCombo->count(); ++i) {
        if (trainCombo->itemData(i).toString().toStdString() == currentTimetable.trainId) {
            trainCombo->setCurrentIndex(i);
            break;
        }
    }
    
    // Wybierz linię
    for (int i = 0; i < lineCombo->count(); ++i) {
        if (lineCombo->itemData(i).toString().toStdString() == currentTimetable.lineId) {
            lineCombo->setCurrentIndex(i);
            break;
        }
    }
    
    // Załaduj przystanki
    currentTimetable.stops.clear();
    const auto& stops = timetable->getStops();
    for (const auto& stop : stops) {
        TimetableStop ts;
        ts.stationId = stop.stationId;
        ts.arrivalTime = stop.arrivalTime;
        ts.departureTime = stop.departureTime;
        ts.platformNumber = stop.platform;
        ts.isOptional = stop.optional;
        currentTimetable.stops.push_back(ts);
    }
    
    updateStopsList();
    updatePreview();
    isModified = false;
}

void TimetableEditor::onTrainChanged(int index) {
    if (index <= 0) return;
    
    currentTimetable.trainId = trainCombo->itemData(index).toString().toStdString();
    isModified = true;
}

void TimetableEditor::onLineChanged(int index) {
    if (index <= 0) return;
    
    currentTimetable.lineId = lineCombo->itemData(index).toString().toStdString();
    
    // Aktualizuj listę stacji na podstawie linii
    // TODO: Filtruj stacje tylko z wybranej linii
    
    isModified = true;
}

void TimetableEditor::onStopAdded() {
    if (stationCombo->currentIndex() <= 0) {
        QMessageBox::warning(this, tr("Błąd"), tr("Wybierz stację"));
        return;
    }
    
    TimetableStop stop;
    stop.stationId = stationCombo->itemData(stationCombo->currentIndex()).toString().toStdString();
    stop.arrivalTime = timeToMinutes(arrivalEdit->time());
    stop.departureTime = timeToMinutes(departureEdit->time());
    stop.platformNumber = platformSpin->value();
    stop.isOptional = optionalCheck->isChecked();
    
    currentTimetable.stops.push_back(stop);
    updateStopsList();
    updatePreview();
    
    isModified = true;
}

void TimetableEditor::onStopRemoved() {
    int row = stopsTable->currentRow();
    if (row < 0 || row >= static_cast<int>(currentTimetable.stops.size())) return;
    
    currentTimetable.stops.erase(currentTimetable.stops.begin() + row);
    updateStopsList();
    updatePreview();
    
    isModified = true;
}

void TimetableEditor::onStopMoved(int direction) {
    int row = stopsTable->currentRow();
    if (row < 0 || row >= static_cast<int>(currentTimetable.stops.size())) return;
    
    int newRow = row + direction;
    if (newRow < 0 || newRow >= static_cast<int>(currentTimetable.stops.size())) return;
    
    std::swap(currentTimetable.stops[row], currentTimetable.stops[newRow]);
    updateStopsList();
    stopsTable->selectRow(newRow);
    updatePreview();
    
    isModified = true;
}

void TimetableEditor::updateStopsList() {
    stopsTable->setRowCount(currentTimetable.stops.size());
    
    GameState* state = game ? game->getGameState() : nullptr;
    
    for (size_t i = 0; i < currentTimetable.stops.size(); ++i) {
        const auto& stop = currentTimetable.stops[i];
        
        // Nazwa stacji
        QString stationName = QString::fromStdString(stop.stationId);
        if (state) {
            auto station = state->getStation(stop.stationId);
            if (station) {
                stationName = QString::fromStdString(station->getName());
            }
        }
        stopsTable->setItem(i, 0, new QTableWidgetItem(stationName));
        
        // Przyjazd
        stopsTable->setItem(i, 1, new QTableWidgetItem(
            minutesToTime(stop.arrivalTime).toString("HH:mm")));
        
        // Odjazd
        stopsTable->setItem(i, 2, new QTableWidgetItem(
            minutesToTime(stop.departureTime).toString("HH:mm")));
        
        // Peron
        stopsTable->setItem(i, 3, new QTableWidgetItem(
            QString::number(stop.platformNumber)));
        
        // Opcjonalny
        QTableWidgetItem* optionalItem = new QTableWidgetItem(
            stop.isOptional ? tr("Tak") : tr("Nie"));
        if (stop.isOptional) {
            optionalItem->setForeground(Qt::gray);
        }
        stopsTable->setItem(i, 4, optionalItem);
    }
}

void TimetableEditor::updatePreview() {
    previewTable->setRowCount(0);
    
    if (currentTimetable.stops.empty()) return;
    
    GameState* state = game ? game->getGameState() : nullptr;
    
    // Pojedynczy kurs
    if (frequencySpin->value() == 0) {
        int totalTime = 0;
        
        for (size_t i = 0; i < currentTimetable.stops.size(); ++i) {
            const auto& stop = currentTimetable.stops[i];
            
            int row = previewTable->rowCount();
            previewTable->insertRow(row);
            
            // Godzina
            QString timeStr = QString("%1 - %2")
                .arg(minutesToTime(stop.arrivalTime).toString("HH:mm"))
                .arg(minutesToTime(stop.departureTime).toString("HH:mm"));
            previewTable->setItem(row, 0, new QTableWidgetItem(timeStr));
            
            // Stacja
            QString stationName = QString::fromStdString(stop.stationId);
            if (state) {
                auto station = state->getStation(stop.stationId);
                if (station) {
                    stationName = QString::fromStdString(station->getName());
                }
            }
            previewTable->setItem(row, 1, new QTableWidgetItem(stationName));
            
            // Peron
            previewTable->setItem(row, 2, new QTableWidgetItem(
                QString::number(stop.platformNumber)));
            
            // Czas podróży
            if (i > 0) {
                int travelTime = stop.arrivalTime - currentTimetable.stops[i-1].departureTime;
                totalTime += travelTime;
                previewTable->setItem(row, 3, new QTableWidgetItem(
                    QString("%1 min").arg(travelTime)));
            } else {
                previewTable->setItem(row, 3, new QTableWidgetItem("-"));
            }
        }
        
        // Podsumowanie
        int lastRow = previewTable->rowCount();
        previewTable->insertRow(lastRow);
        QTableWidgetItem* summaryItem = new QTableWidgetItem(
            tr("Całkowity czas: %1 min").arg(totalTime));
        summaryItem->setFont(QFont("Arial", 10, QFont::Bold));
        previewTable->setItem(lastRow, 0, summaryItem);
        
    } else {
        // Wielokrotne kursy
        int firstMinutes = timeToMinutes(firstDepartureEdit->time());
        int lastMinutes = timeToMinutes(lastDepartureEdit->time());
        int frequency = frequencySpin->value();
        
        int courseCount = 0;
        for (int time = firstMinutes; time <= lastMinutes; time += frequency) {
            courseCount++;
        }
        
        QTableWidgetItem* headerItem = new QTableWidgetItem(
            tr("Liczba kursów: %1 (co %2 min)")
                .arg(courseCount)
                .arg(frequency));
        headerItem->setFont(QFont("Arial", 10, QFont::Bold));
        
        int row = previewTable->rowCount();
        previewTable->insertRow(row);
        previewTable->setItem(row, 0, headerItem);
    }
}

void TimetableEditor::onCalculateTimes() {
    if (currentTimetable.stops.size() < 2) {
        QMessageBox::warning(this, tr("Błąd"), 
            tr("Dodaj przynajmniej dwa przystanki"));
        return;
    }
    
    calculateTravelTimes();
    updateStopsList();
    updatePreview();
    
    QMessageBox::information(this, tr("Obliczanie czasów"),
        tr("Czasy przejazdu zostały obliczone automatycznie"));
}

void TimetableEditor::calculateTravelTimes() {
    if (!game || !game->getGameState()) return;
    
    GameState* state = game->getGameState();
    
    // Pobierz pociąg
    auto train = state->getTrain(currentTimetable.trainId);
    if (!train) return;
    
    float trainMaxSpeed = train->getMaxSpeed();
    
    // Oblicz czasy między przystankami
    for (size_t i = 1; i < currentTimetable.stops.size(); ++i) {
        auto& prevStop = currentTimetable.stops[i-1];
        auto& currStop = currentTimetable.stops[i];
        
        // Znajdź stacje
        auto prevStation = state->getStation(prevStop.stationId);
        auto currStation = state->getStation(currStop.stationId);
        
        if (!prevStation || !currStation) continue;
        
        // Oblicz odległość (uproszczone)
        double lat1 = prevStation->getLatitude();
        double lon1 = prevStation->getLongitude();
        double lat2 = currStation->getLatitude();
        double lon2 = currStation->getLongitude();
        
        const double R = 6371.0; // km
        double dLat = (lat2 - lat1) * M_PI / 180.0;
        double dLon = (lon2 - lon1) * M_PI / 180.0;
        double a = sin(dLat/2) * sin(dLat/2) +
                   cos(lat1 * M_PI / 180.0) * cos(lat2 * M_PI / 180.0) *
                   sin(dLon/2) * sin(dLon/2);
        double c = 2 * atan2(sqrt(a), sqrt(1-a));
        double distance = R * c;
        
        // Oblicz czas (z uwzględnieniem przyspieszania/hamowania)
        float avgSpeed = trainMaxSpeed * 0.7f; // 70% prędkości max
        float travelTime = (distance / avgSpeed) * 60.0f; // minuty
        
        // Dodaj czas na postój
        int stopTime = 2; // 2 minuty postoju
        if (prevStation->getType() == StationType::MAJOR) {
            stopTime = 5;
        }
        
        // Ustaw czasy
        currStop.arrivalTime = prevStop.departureTime + static_cast<int>(travelTime);
        currStop.departureTime = currStop.arrivalTime + stopTime;
    }
}

void TimetableEditor::onValidateTimetable() {
    if (validateTimetable()) {
        QMessageBox::information(this, tr("Walidacja"),
            tr("Rozkład jazdy jest poprawny"));
    }
}

bool TimetableEditor::validateTimetable() {
    // Sprawdź podstawowe dane
    if (currentTimetable.name.empty()) {
        QMessageBox::warning(this, tr("Błąd"), tr("Podaj nazwę rozkładu"));
        return false;
    }
    
    if (currentTimetable.trainId.empty()) {
        QMessageBox::warning(this, tr("Błąd"), tr("Wybierz pociąg"));
        return false;
    }
    
    if (currentTimetable.lineId.empty()) {
        QMessageBox::warning(this, tr("Błąd"), tr("Wybierz linię"));
        return false;
    }
    
    if (currentTimetable.stops.size() < 2) {
        QMessageBox::warning(this, tr("Błąd"), 
            tr("Rozkład musi mieć przynajmniej 2 przystanki"));
        return false;
    }
    
    // Sprawdź kolejność czasów
    for (size_t i = 0; i < currentTimetable.stops.size(); ++i) {
        const auto& stop = currentTimetable.stops[i];
        
        if (stop.arrivalTime > stop.departureTime) {
            QMessageBox::warning(this, tr("Błąd"),
                tr("Czas odjazdu nie może być wcześniejszy niż przyjazdu (przystanek %1)")
                    .arg(i + 1));
            return false;
        }
        
        if (i > 0) {
            const auto& prevStop = currentTimetable.stops[i-1];
            if (stop.arrivalTime < prevStop.departureTime) {
                QMessageBox::warning(this, tr("Błąd"),
                    tr("Czasy przystanków nachodzą na siebie (przystanki %1-%2)")
                        .arg(i).arg(i + 1));
                return false;
            }
        }
    }
    
    // TODO: Sprawdź dostępność peronów
    // TODO: Sprawdź konflikty z innymi rozkładami
    
    return true;
}

void TimetableEditor::onApplyTimetable() {
    if (!validateTimetable()) {
        return;
    }
    
    saveTimetable();
    
    QMessageBox::information(this, tr("Sukces"),
        tr("Rozkład jazdy został zapisany"));
    
    emit timetableModified(QString::fromStdString(currentTimetable.id));
    
    isModified = false;
    loadTimetables(); // Odśwież listę
}

void TimetableEditor::saveTimetable() {
    if (!game || !game->getGameState()) return;
    
    // TODO: Konwersja TimetableEntry na Timetable i zapis w GameState
    
    // Tymczasowo tylko log
    LOG_INFO("Zapisano rozkład: " + currentTimetable.name);
}

int TimetableEditor::timeToMinutes(const QTime& time) const {
    return time.hour() * 60 + time.minute();
}

QTime TimetableEditor::minutesToTime(int minutes) const {
    return QTime(minutes / 60, minutes % 60);
}

void TimetableEditor::refresh() {
    loadTimetables();
    populateTrains();
    populateLines();
    populateStations();
}

// NewTimetableDialog
NewTimetableDialog::NewTimetableDialog(Game* game, QWidget* parent)
    : QDialog(parent) {
    
    setWindowTitle(tr("Nowy rozkład jazdy"));
    setModal(true);
    
    QVBoxLayout* layout = new QVBoxLayout(this);
    
    // Nazwa
    layout->addWidget(new QLabel(tr("Nazwa rozkładu:"), this));
    nameEdit = new QLineEdit(this);
    layout->addWidget(nameEdit);
    
    // Pociąg
    layout->addWidget(new QLabel(tr("Pociąg:"), this));
    trainCombo = new QComboBox(this);
    trainCombo->addItem(tr("-- Wybierz pociąg --"));
    
    if (game && game->getGameState()) {
        const auto& trains = game->getGameState()->getAllTrains();
        for (const auto& train : trains) {
            if (train->isAvailable()) {
                trainCombo->addItem(
                    QString::fromStdString(train->getName()),
                    QString::fromStdString(train->getId())
                );
            }
        }
    }
    layout->addWidget(trainCombo);
    
    // Linia
    layout->addWidget(new QLabel(tr("Linia:"), this));
    lineCombo = new QComboBox(this);
    lineCombo->addItem(tr("-- Wybierz linię --"));
    
    if (game && game->getGameState()) {
        const auto& lines = game->getGameState()->getAllLines();
        for (const auto& line : lines) {
            lineCombo->addItem(
                QString::fromStdString(line->getName()),
                QString::fromStdString(line->getId())
            );
        }
    }
    layout->addWidget(lineCombo);
    
    // Przyciski
    QDialogButtonBox* buttons = new QDialogButtonBox(
        QDialogButtonBox::Ok | QDialogButtonBox::Cancel, this);
    connect(buttons, &QDialogButtonBox::accepted, this, &QDialog::accept);
    connect(buttons, &QDialogButtonBox::rejected, this, &QDialog::reject);
    layout->addWidget(buttons);
}

QString NewTimetableDialog::getName() const {
    return nameEdit->text();
}

QString NewTimetableDialog::getTrainId() const {
    return trainCombo->currentData().toString();
}

QString NewTimetableDialog::getLineId() const {
    return lineCombo->currentData().toString();
}

// TimetableGraphWidget
TimetableGraphWidget::TimetableGraphWidget(QWidget* parent) : QWidget(parent) {
    setMinimumHeight(300);
}

void TimetableGraphWidget::setTimetable(const TimetableEntry& tt) {
    timetable = tt;
    update();
}

void TimetableGraphWidget::setStations(const std::vector<Station*>& s) {
    stations = s;
    update();
}

void TimetableGraphWidget::paintEvent(QPaintEvent* event) {
    QPainter painter(this);
    painter.fillRect(rect(), Qt::white);
    
    if (timetable.stops.empty() || stations.empty()) {
        painter.drawText(rect(), Qt::AlignCenter, tr("Brak danych"));
        return;
    }
    
    // Rysuj siatkę
    painter.setPen(QPen(Qt::lightGray, 1));
    
    // Linie poziome (stacje)
    for (size_t i = 0; i < stations.size(); ++i) {
        int y = stationToY(stations[i]->getId());
        painter.drawLine(0, y, width(), y);
        
        // Nazwa stacji
        painter.drawText(5, y - 5, QString::fromStdString(stations[i]->getName()));
    }
    
    // Linie pionowe (godziny)
    for (int hour = 0; hour < 24; ++hour) {
        int x = timeToX(hour * 60);
        painter.drawLine(x, 0, x, height());
        
        // Godzina
        painter.drawText(x - 10, height() - 5, QString::number(hour));
    }
    
    // Rysuj trasę pociągu
    painter.setPen(QPen(Qt::blue, 2));
    
    for (size_t i = 1; i < timetable.stops.size(); ++i) {
        const auto& prevStop = timetable.stops[i-1];
        const auto& currStop = timetable.stops[i];
        
        int x1 = timeToX(prevStop.departureTime);
        int y1 = stationToY(prevStop.stationId);
        int x2 = timeToX(currStop.arrivalTime);
        int y2 = stationToY(currStop.stationId);
        
        painter.drawLine(x1, y1, x2, y2);
        
        // Postój na stacji
        if (currStop.departureTime > currStop.arrivalTime) {
            int x3 = timeToX(currStop.departureTime);
            painter.drawLine(x2, y2, x3, y2);
        }
    }
}

int TimetableGraphWidget::timeToX(int minutes) const {
    return 50 + (width() - 100) * minutes / (24 * 60);
}

int TimetableGraphWidget::stationToY(const std::string& stationId) const {
    for (size_t i = 0; i < stations.size(); ++i) {
        if (stations[i]->getId() == stationId) {
            return 30 + i * (height() - 60) / stations.size();
        }
    }
    return 0;
}