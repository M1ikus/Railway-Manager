#ifndef TIMETABLEEDITOR_H
#define TIMETABLEEDITOR_H

#include <QWidget>
#include <memory>

QT_BEGIN_NAMESPACE
class QTableWidget;
class QTreeWidget;
class QListWidget;
class QComboBox;
class QPushButton;
class QTimeEdit;
class QSpinBox;
class QCheckBox;
class QGroupBox;
class QSplitter;
QT_END_NAMESPACE

// Forward declarations
class Game;
class Timetable;
class Train;
class Station;
class Line;

struct TimetableStop {
    std::string stationId;
    int arrivalTime;      // minuty od północy
    int departureTime;    // minuty od północy
    int platformNumber;
    bool isOptional;
};

struct TimetableEntry {
    std::string id;
    std::string name;
    std::string trainId;
    std::string lineId;
    std::vector<TimetableStop> stops;
    bool isActive;
    int frequency;        // co ile minut (0 = pojedynczy kurs)
    int firstDeparture;   // pierwsza godzina odjazdu
    int lastDeparture;    // ostatnia godzina odjazdu
};

class TimetableEditor : public QWidget {
    Q_OBJECT
    
public:
    explicit TimetableEditor(Game* game, QWidget* parent = nullptr);
    ~TimetableEditor();
    
    void refresh();
    
signals:
    void timetableCreated(const QString& timetableId);
    void timetableModified(const QString& timetableId);
    void timetableDeleted(const QString& timetableId);
    
private slots:
    void onNewTimetable();
    void onEditTimetable();
    void onDeleteTimetable();
    void onDuplicateTimetable();
    void onImportTimetable();
    void onExportTimetable();
    
    void onTimetableSelected();
    void onTrainChanged(int index);
    void onLineChanged(int index);
    void onStopAdded();
    void onStopRemoved();
    void onStopMoved(int direction);
    
    void onCalculateTimes();
    void onValidateTimetable();
    void onApplyTimetable();
    
    void updateStopsList();
    void updatePreview();
    
private:
    void setupUI();
    void createTimetableList();
    void createEditorPanel();
    void createStopsEditor();
    void createPreviewPanel();
    void connectSignals();
    
    void loadTimetables();
    void loadTimetable(const std::string& timetableId);
    void saveTimetable();
    
    void populateTrains();
    void populateLines();
    void populateStations();
    
    int timeToMinutes(const QTime& time) const;
    QTime minutesToTime(int minutes) const;
    
    bool validateTimetable();
    void calculateTravelTimes();
    
    Game* game;
    
    // Główne komponenty
    QSplitter* mainSplitter;
    
    // Lista rozkładów
    QTreeWidget* timetableTree;
    QPushButton* newBtn;
    QPushButton* editBtn;
    QPushButton* deleteBtn;
    QPushButton* duplicateBtn;
    
    // Panel edytora
    QGroupBox* editorGroup;
    QLineEdit* nameEdit;
    QComboBox* trainCombo;
    QComboBox* lineCombo;
    QCheckBox* activeCheck;
    
    // Edytor przystanków
    QTableWidget* stopsTable;
    QComboBox* stationCombo;
    QTimeEdit* arrivalEdit;
    QTimeEdit* departureEdit;
    QSpinBox* platformSpin;
    QCheckBox* optionalCheck;
    QPushButton* addStopBtn;
    QPushButton* removeStopBtn;
    QPushButton* moveUpBtn;
    QPushButton* moveDownBtn;
    
    // Częstotliwość
    QGroupBox* frequencyGroup;
    QSpinBox* frequencySpin;
    QTimeEdit* firstDepartureEdit;
    QTimeEdit* lastDepartureEdit;
    
    // Podgląd
    QTableWidget* previewTable;
    QPushButton* calculateBtn;
    QPushButton* validateBtn;
    QPushButton* applyBtn;
    
    // Dane
    TimetableEntry currentTimetable;
    bool isModified = false;
};

// Dialog tworzenia nowego rozkładu
class NewTimetableDialog : public QDialog {
    Q_OBJECT
    
public:
    explicit NewTimetableDialog(Game* game, QWidget* parent = nullptr);
    
    QString getName() const;
    QString getTrainId() const;
    QString getLineId() const;
    
private:
    QLineEdit* nameEdit;
    QComboBox* trainCombo;
    QComboBox* lineCombo;
};

// Widget podglądu graficznego rozkładu
class TimetableGraphWidget : public QWidget {
    Q_OBJECT
    
public:
    explicit TimetableGraphWidget(QWidget* parent = nullptr);
    
    void setTimetable(const TimetableEntry& timetable);
    void setStations(const std::vector<Station*>& stations);
    
protected:
    void paintEvent(QPaintEvent* event) override;
    
private:
    TimetableEntry timetable;
    std::vector<Station*> stations;
    
    int timeToX(int minutes) const;
    int stationToY(const std::string& stationId) const;
};

#endif // TIMETABLEEDITOR_H