#ifndef EVENT_H
#define EVENT_H

#include <string>
#include <vector>
#include <functional>
#include <chrono>

// Forward declarations
class GameState;

enum class EventType {
    // Wydarzenia losowe
    WEATHER,              // Pogoda (śnieg, burza, mgła)
    ACCIDENT,             // Wypadek
    BREAKDOWN,            // Awaria
    STRIKE,               // Strajk
    PASSENGER_INCIDENT,   // Incydent z pasażerem
    
    // Wydarzenia ekonomiczne
    FUEL_PRICE_CHANGE,    // Zmiana cen paliwa
    COMPETITION,          // Nowa konkurencja
    SUBSIDY,              // Dotacja
    TAX_CHANGE,           // Zmiana podatków
    
    // Wydarzenia społeczne
    FESTIVAL,             // Festiwal/wydarzenie
    HOLIDAY,              // Święto
    PROTEST,              // Protest
    MEDIA_COVERAGE,       // Relacja medialna
    
    // Wydarzenia infrastrukturalne
    CONSTRUCTION,         // Budowa/remont
    LINE_CLOSURE,         // Zamknięcie linii
    STATION_UPGRADE,      // Modernizacja stacji
    NEW_CONNECTION,       // Nowe połączenie
    
    // Wydarzenia specjalne
    VIP_TRANSPORT,        // Transport VIP
    INSPECTION,           // Kontrola/inspekcja
    ACHIEVEMENT,          // Osiągnięcie
    MILESTONE             // Kamień milowy
};

enum class EventSeverity {
    INFO,                 // Informacyjne
    LOW,                  // Niskie
    MEDIUM,               // Średnie
    HIGH,                 // Wysokie
    CRITICAL              // Krytyczne
};

enum class EventScope {
    SYSTEM_WIDE,          // Całe system
    LINE_SPECIFIC,        // Konkretna linia
    STATION_SPECIFIC,     // Konkretna stacja
    TRAIN_SPECIFIC,       // Konkretny pociąg
    REGION_SPECIFIC       // Konkretny region
};

struct EventEffect {
    std::string type;     // "money", "reputation", "delay", "cancel", etc.
    float value;          // Wartość efektu
    int duration;         // Czas trwania w minutach (0 = natychmiastowy)
    std::string target;   // ID celu (opcjonalne)
};

struct EventChoice {
    std::string id;
    std::string text;
    std::vector<EventEffect> effects;
    float cost;          // Koszt wyboru
    bool available;      // Czy dostępne
};

class Event {
public:
    Event(const std::string& id, const std::string& title, EventType type);
    ~Event();
    
    // Podstawowe gettery
    const std::string& getId() const { return id; }
    const std::string& getTitle() const { return title; }
    const std::string& getDescription() const { return description; }
    EventType getType() const { return type; }
    EventSeverity getSeverity() const { return severity; }
    EventScope getScope() const { return scope; }
    
    // Podstawowe settery
    void setTitle(const std::string& t) { title = t; }
    void setDescription(const std::string& d) { description = d; }
    void setSeverity(EventSeverity s) { severity = s; }
    void setScope(EventScope s) { scope = s; }
    
    // Warunki wystąpienia
    void setProbability(float prob) { probability = std::max(0.0f, std::min(1.0f, prob)); }
    float getProbability() const { return probability; }
    
    void setMinDaysBetween(int days) { minDaysBetween = days; }
    int getMinDaysBetween() const { return minDaysBetween; }
    
    void addRequirement(const std::string& req) { requirements.push_back(req); }
    const std::vector<std::string>& getRequirements() const { return requirements; }
    
    // Efekty
    void addEffect(const EventEffect& effect) { effects.push_back(effect); }
    const std::vector<EventEffect>& getEffects() const { return effects; }
    void clearEffects() { effects.clear(); }
    
    // Wybory gracza
    void addChoice(const EventChoice& choice) { choices.push_back(choice); }
    const std::vector<EventChoice>& getChoices() const { return choices; }
    bool hasChoices() const { return !choices.empty(); }
    
    // Cele wydarzenia
    void setTargetStation(const std::string& stationId) { targetStationId = stationId; }
    void setTargetTrain(const std::string& trainId) { targetTrainId = trainId; }
    void setTargetLine(const std::string& lineId) { targetLineId = lineId; }
    void setTargetRegion(const std::string& region) { targetRegion = region; }
    
    const std::string& getTargetStation() const { return targetStationId; }
    const std::string& getTargetTrain() const { return targetTrainId; }
    const std::string& getTargetLine() const { return targetLineId; }
    const std::string& getTargetRegion() const { return targetRegion; }
    
    // Czas trwania
    void setDuration(int minutes) { duration = minutes; }
    int getDuration() const { return duration; }
    bool isTemporary() const { return duration > 0; }
    
    // Media i grafika
    void setImagePath(const std::string& path) { imagePath = path; }
    const std::string& getImagePath() const { return imagePath; }
    
    void setSoundPath(const std::string& path) { soundPath = path; }
    const std::string& getSoundPath() const { return soundPath; }
    
    // Triggery
    void setTriggerCondition(std::function<bool(GameState*)> condition) { 
        triggerCondition = condition; 
    }
    bool canTrigger(GameState* state) const;
    
    // Wykonanie
    void execute(GameState* state);
    void executeChoice(GameState* state, const std::string& choiceId);
    
    // Historia
    void recordOccurrence() { 
        lastOccurrence = std::chrono::system_clock::now(); 
        occurrenceCount++;
    }
    
    std::chrono::system_clock::time_point getLastOccurrence() const { return lastOccurrence; }
    int getOccurrenceCount() const { return occurrenceCount; }
    bool canOccurNow() const;
    
    // Serializacja
    std::string toJSON() const;
    bool fromJSON(const std::string& json);
    
private:
    // Podstawowe dane
    std::string id;
    std::string title;
    std::string description;
    EventType type;
    EventSeverity severity;
    EventScope scope;
    
    // Prawdopodobieństwo i częstotliwość
    float probability = 0.01f;        // 1% domyślnie
    int minDaysBetween = 7;           // Minimum 7 dni między wystąpieniami
    std::vector<std::string> requirements;  // Wymagania do wystąpienia
    
    // Efekty
    std::vector<EventEffect> effects;
    std::vector<EventChoice> choices;
    
    // Cele
    std::string targetStationId;
    std::string targetTrainId;
    std::string targetLineId;
    std::string targetRegion;
    
    // Czas trwania
    int duration = 0;                 // 0 = natychmiastowe
    
    // Media
    std::string imagePath;
    std::string soundPath;
    
    // Trigger
    std::function<bool(GameState*)> triggerCondition;
    
    // Historia
    std::chrono::system_clock::time_point lastOccurrence;
    int occurrenceCount = 0;
    
    // Pomocnicze
    void applyEffect(GameState* state, const EventEffect& effect);
    bool checkRequirements(GameState* state) const;
};

// Przykładowe fabryki wydarzeń
namespace EventFactory {
    std::unique_ptr<Event> createWeatherEvent(const std::string& weatherType);
    std::unique_ptr<Event> createAccidentEvent(const std::string& severity);
    std::unique_ptr<Event> createEconomicEvent(const std::string& economicType);
    std::unique_ptr<Event> createSpecialEvent(const std::string& specialType);
}

#endif // EVENT_H