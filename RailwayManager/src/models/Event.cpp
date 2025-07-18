#include "Event.h"
#include "core/GameState.h"
#include "models/Train.h"
#include "models/Station.h"
#include "models/Line.h"
#include "utils/Logger.h"
#include <nlohmann/json.hpp>
#include <random>

using json = nlohmann::json;

Event::Event(const std::string& id, const std::string& title, EventType type)
    : id(id), title(title), type(type), severity(EventSeverity::MEDIUM), 
      scope(EventScope::SYSTEM_WIDE) {
}

Event::~Event() {
}

bool Event::canTrigger(GameState* state) const {
    if (!state) return false;
    
    // Sprawdź funkcję warunkową
    if (triggerCondition && !triggerCondition(state)) {
        return false;
    }
    
    // Sprawdź wymagania
    if (!checkRequirements(state)) {
        return false;
    }
    
    // Sprawdź minimalny czas od ostatniego wystąpienia
    if (!canOccurNow()) {
        return false;
    }
    
    // Sprawdź prawdopodobieństwo
    static std::random_device rd;
    static std::mt19937 gen(rd());
    static std::uniform_real_distribution<> dis(0.0, 1.0);
    
    return dis(gen) <= probability;
}

bool Event::canOccurNow() const {
    if (occurrenceCount == 0) {
        return true; // Pierwsze wystąpienie
    }
    
    auto now = std::chrono::system_clock::now();
    auto daysSince = std::chrono::duration_cast<std::chrono::hours>(
        now - lastOccurrence).count() / 24;
    
    return daysSince >= minDaysBetween;
}

void Event::execute(GameState* state) {
    if (!state) return;
    
    LOG_INFO("Wykonywanie wydarzenia: " + title);
    
    // Zastosuj efekty
    for (const auto& effect : effects) {
        applyEffect(state, effect);
    }
    
    // Zapisz wystąpienie
    recordOccurrence();
    
    // Jeśli wydarzenie ma czas trwania, zaplanuj zakończenie
    if (duration > 0) {
        // TODO: Zaplanuj zakończenie wydarzenia
    }
}

void Event::executeChoice(GameState* state, const std::string& choiceId) {
    if (!state) return;
    
    // Znajdź wybór
    auto it = std::find_if(choices.begin(), choices.end(),
        [&choiceId](const EventChoice& c) { return c.id == choiceId; });
    
    if (it == choices.end()) {
        LOG_WARNING("Nie znaleziono wyboru: " + choiceId);
        return;
    }
    
    const EventChoice& choice = *it;
    
    // Sprawdź czy gracz może sobie pozwolić
    if (choice.cost > 0 && !state->canAfford(choice.cost)) {
        LOG_WARNING("Gracz nie może sobie pozwolić na wybór: " + choiceId);
        return;
    }
    
    // Zapłać koszt
    if (choice.cost > 0) {
        state->addMoney(-choice.cost);
    }
    
    // Zastosuj efekty wyboru
    for (const auto& effect : choice.effects) {
        applyEffect(state, effect);
    }
    
    LOG_INFO("Wykonano wybór: " + choice.text);
}

void Event::applyEffect(GameState* state, const EventEffect& effect) {
    if (effect.type == "money") {
        state->addMoney(effect.value);
        LOG_INFO("Zmiana salda: " + std::to_string(effect.value));
        
    } else if (effect.type == "reputation") {
        state->changeReputation(static_cast<int>(effect.value));
        LOG_INFO("Zmiana reputacji: " + std::to_string(effect.value));
        
    } else if (effect.type == "delay") {
        // Opóźnij pociąg
        if (!effect.target.empty()) {
            auto train = state->getTrain(effect.target);
            if (train) {
                train->setDelay(static_cast<int>(effect.value));
                LOG_INFO("Pociąg " + effect.target + " opóźniony o " + 
                        std::to_string(effect.value) + " minut");
            }
        }
        
    } else if (effect.type == "cancel") {
        // Odwołaj kurs
        // TODO: Implementacja odwoływania kursów
        
    } else if (effect.type == "block_line") {
        // Zablokuj linię
        if (!effect.target.empty()) {
            auto line = state->getLine(effect.target);
            if (line) {
                line->setStatus(LineStatus::BLOCKED);
                LOG_INFO("Linia " + effect.target + " zablokowana");
            }
        }
        
    } else if (effect.type == "damage_train") {
        // Uszkodź pociąg
        if (!effect.target.empty()) {
            auto train = state->getTrain(effect.target);
            if (train) {
                train->setCondition(train->getCondition() * (1.0f - effect.value));
                LOG_INFO("Pociąg " + effect.target + " uszkodzony");
            }
        }
        
    } else if (effect.type == "passenger_satisfaction") {
        // Zmień zadowolenie pasażerów
        // TODO: Implementacja systemu zadowolenia
        
    } else {
        LOG_WARNING("Nieznany typ efektu: " + effect.type);
    }
}

bool Event::checkRequirements(GameState* state) const {
    for (const auto& req : requirements) {
        // Parsuj wymaganie (format: "typ:wartość")
        size_t colonPos = req.find(':');
        if (colonPos == std::string::npos) continue;
        
        std::string reqType = req.substr(0, colonPos);
        std::string reqValue = req.substr(colonPos + 1);
        
        if (reqType == "min_trains") {
            int minTrains = std::stoi(reqValue);
            if (state->getAllTrains().size() < static_cast<size_t>(minTrains)) {
                return false;
            }
            
        } else if (reqType == "min_money") {
            double minMoney = std::stod(reqValue);
            if (state->getMoney() < minMoney) {
                return false;
            }
            
        } else if (reqType == "min_reputation") {
            int minRep = std::stoi(reqValue);
            if (state->getCompanyInfo().reputation < minRep) {
                return false;
            }
            
        } else if (reqType == "has_station") {
            if (!state->getStation(reqValue)) {
                return false;
            }
            
        } else if (reqType == "has_line") {
            if (!state->getLine(reqValue)) {
                return false;
            }
            
        } else if (reqType == "season") {
            // TODO: Sprawdź porę roku
            
        } else if (reqType == "time_of_day") {
            // TODO: Sprawdź porę dnia
        }
    }
    
    return true;
}

std::string Event::toJSON() const {
    json j;
    
    j["id"] = id;
    j["title"] = title;
    j["description"] = description;
    j["type"] = static_cast<int>(type);
    j["severity"] = static_cast<int>(severity);
    j["scope"] = static_cast<int>(scope);
    j["probability"] = probability;
    j["minDaysBetween"] = minDaysBetween;
    j["duration"] = duration;
    
    // Wymagania
    j["requirements"] = requirements;
    
    // Efekty
    json effectsArray = json::array();
    for (const auto& effect : effects) {
        json e;
        e["type"] = effect.type;
        e["value"] = effect.value;
        e["duration"] = effect.duration;
        e["target"] = effect.target;
        effectsArray.push_back(e);
    }
    j["effects"] = effectsArray;
    
    // Wybory
    json choicesArray = json::array();
    for (const auto& choice : choices) {
        json c;
        c["id"] = choice.id;
        c["text"] = choice.text;
        c["cost"] = choice.cost;
        c["available"] = choice.available;
        
        json choiceEffects = json::array();
        for (const auto& effect : choice.effects) {
            json e;
            e["type"] = effect.type;
            e["value"] = effect.value;
            e["duration"] = effect.duration;
            e["target"] = effect.target;
            choiceEffects.push_back(e);
        }
        c["effects"] = choiceEffects;
        
        choicesArray.push_back(c);
    }
    j["choices"] = choicesArray;
    
    // Cele
    if (!targetStationId.empty()) j["targetStation"] = targetStationId;
    if (!targetTrainId.empty()) j["targetTrain"] = targetTrainId;
    if (!targetLineId.empty()) j["targetLine"] = targetLineId;
    if (!targetRegion.empty()) j["targetRegion"] = targetRegion;
    
    // Media
    if (!imagePath.empty()) j["image"] = imagePath;
    if (!soundPath.empty()) j["sound"] = soundPath;
    
    return j.dump(2);
}

bool Event::fromJSON(const std::string& jsonStr) {
    try {
        json j = json::parse(jsonStr);
        
        id = j["id"];
        title = j["title"];
        description = j.value("description", "");
        type = static_cast<EventType>(j["type"]);
        severity = static_cast<EventSeverity>(j.value("severity", 1));
        scope = static_cast<EventScope>(j.value("scope", 0));
        probability = j.value("probability", 0.01f);
        minDaysBetween = j.value("minDaysBetween", 7);
        duration = j.value("duration", 0);
        
        // Wymagania
        if (j.contains("requirements")) {
            requirements = j["requirements"].get<std::vector<std::string>>();
        }
        
        // Efekty
        effects.clear();
        if (j.contains("effects")) {
            for (const auto& e : j["effects"]) {
                EventEffect effect;
                effect.type = e["type"];
                effect.value = e["value"];
                effect.duration = e.value("duration", 0);
                effect.target = e.value("target", "");
                effects.push_back(effect);
            }
        }
        
        // Wybory
        choices.clear();
        if (j.contains("choices")) {
            for (const auto& c : j["choices"]) {
                EventChoice choice;
                choice.id = c["id"];
                choice.text = c["text"];
                choice.cost = c.value("cost", 0.0f);
                choice.available = c.value("available", true);
                
                if (c.contains("effects")) {
                    for (const auto& e : c["effects"]) {
                        EventEffect effect;
                        effect.type = e["type"];
                        effect.value = e["value"];
                        effect.duration = e.value("duration", 0);
                        effect.target = e.value("target", "");
                        choice.effects.push_back(effect);
                    }
                }
                
                choices.push_back(choice);
            }
        }
        
        // Cele
        targetStationId = j.value("targetStation", "");
        targetTrainId = j.value("targetTrain", "");
        targetLineId = j.value("targetLine", "");
        targetRegion = j.value("targetRegion", "");
        
        // Media
        imagePath = j.value("image", "");
        soundPath = j.value("sound", "");
        
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd parsowania JSON wydarzenia: " + std::string(e.what()));
        return false;
    }
}

// Implementacje fabryk wydarzeń
namespace EventFactory {

std::unique_ptr<Event> createWeatherEvent(const std::string& weatherType) {
    auto event = std::make_unique<Event>("weather_" + weatherType, 
                                        "Warunki pogodowe: " + weatherType, 
                                        EventType::WEATHER);
    
    if (weatherType == "snow") {
        event->setDescription("Intensywne opady śniegu utrudniają ruch pociągów");
        event->setSeverity(EventSeverity::HIGH);
        event->setProbability(0.05f); // 5% zimą
        event->setDuration(180); // 3 godziny
        
        EventEffect effect;
        effect.type = "delay";
        effect.value = 15.0f; // 15 minut opóźnienia
        effect.duration = 180;
        event->addEffect(effect);
        
    } else if (weatherType == "storm") {
        event->setDescription("Burza z piorunami - zagrożenie dla bezpieczeństwa");
        event->setSeverity(EventSeverity::CRITICAL);
        event->setProbability(0.02f);
        event->setDuration(60);
        
        EventEffect effect;
        effect.type = "cancel";
        effect.value = 1.0f;
        effect.duration = 60;
        event->addEffect(effect);
        
    } else if (weatherType == "fog") {
        event->setDescription("Gęsta mgła ogranicza widoczność");
        event->setSeverity(EventSeverity::MEDIUM);
        event->setProbability(0.08f);
        event->setDuration(120);
        
        EventEffect effect;
        effect.type = "delay";
        effect.value = 5.0f;
        effect.duration = 120;
        event->addEffect(effect);
    }
    
    return event;
}

std::unique_ptr<Event> createAccidentEvent(const std::string& severity) {
    auto event = std::make_unique<Event>("accident_" + severity,
                                        "Wypadek kolejowy",
                                        EventType::ACCIDENT);
    
    if (severity == "minor") {
        event->setDescription("Drobna kolizja - brak rannych");
        event->setSeverity(EventSeverity::LOW);
        event->setProbability(0.001f);
        
        EventEffect moneyEffect;
        moneyEffect.type = "money";
        moneyEffect.value = -50000.0f;
        event->addEffect(moneyEffect);
        
        EventEffect repEffect;
        repEffect.type = "reputation";
        repEffect.value = -5.0f;
        event->addEffect(repEffect);
        
    } else if (severity == "major") {
        event->setDescription("Poważny wypadek - są ranni");
        event->setSeverity(EventSeverity::CRITICAL);
        event->setProbability(0.0001f);
        
        EventEffect moneyEffect;
        moneyEffect.type = "money";
        moneyEffect.value = -500000.0f;
        event->addEffect(moneyEffect);
        
        EventEffect repEffect;
        repEffect.type = "reputation";
        repEffect.value = -20.0f;
        event->addEffect(repEffect);
        
        // Dodaj wybory
        EventChoice choice1;
        choice1.id = "full_compensation";
        choice1.text = "Wypłać pełne odszkodowania";
        choice1.cost = 200000.0f;
        
        EventEffect compEffect;
        compEffect.type = "reputation";
        compEffect.value = 10.0f;
        choice1.effects.push_back(compEffect);
        
        event->addChoice(choice1);
        
        EventChoice choice2;
        choice2.id = "minimal_compensation";
        choice2.text = "Wypłać minimalne odszkodowania";
        choice2.cost = 50000.0f;
        
        EventEffect minEffect;
        minEffect.type = "reputation";
        minEffect.value = -10.0f;
        choice2.effects.push_back(minEffect);
        
        event->addChoice(choice2);
    }
    
    return event;
}

std::unique_ptr<Event> createEconomicEvent(const std::string& economicType) {
    auto event = std::make_unique<Event>("economic_" + economicType,
                                        "Wydarzenie ekonomiczne",
                                        EventType::SUBSIDY);
    
    if (economicType == "subsidy") {
        event->setTitle("Dotacja rządowa");
        event->setDescription("Rząd przyznał dotację na rozwój transportu kolejowego");
        event->setSeverity(EventSeverity::INFO);
        event->setProbability(0.01f);
        
        EventEffect effect;
        effect.type = "money";
        effect.value = 250000.0f;
        event->addEffect(effect);
        
        event->addRequirement("min_reputation:60");
        event->addRequirement("min_trains:5");
        
    } else if (economicType == "fuel_increase") {
        event->setTitle("Wzrost cen paliwa");
        event->setDescription("Ceny paliwa wzrosły o 15%");
        event->setSeverity(EventSeverity::MEDIUM);
        event->setProbability(0.05f);
        event->setDuration(30 * 24 * 60); // 30 dni
        
        // TODO: Implementacja wzrostu kosztów operacyjnych
    }
    
    return event;
}

std::unique_ptr<Event> createSpecialEvent(const std::string& specialType) {
    auto event = std::make_unique<Event>("special_" + specialType,
                                        "Wydarzenie specjalne",
                                        EventType::SPECIAL);
    
    if (specialType == "vip_transport") {
        event->setTitle("Transport VIP");
        event->setDescription("Ważna osobistość chce skorzystać z Twoich usług");
        event->setSeverity(EventSeverity::INFO);
        event->setProbability(0.005f);
        
        EventChoice acceptChoice;
        acceptChoice.id = "accept_vip";
        acceptChoice.text = "Zorganizuj transport VIP";
        acceptChoice.cost = -50000.0f; // Zysk
        
        EventEffect moneyEffect;
        moneyEffect.type = "money";
        moneyEffect.value = 50000.0f;
        acceptChoice.effects.push_back(moneyEffect);
        
        EventEffect repEffect;
        repEffect.type = "reputation";
        repEffect.value = 15.0f;
        acceptChoice.effects.push_back(repEffect);
        
        event->addChoice(acceptChoice);
        
        EventChoice declineChoice;
        declineChoice.id = "decline_vip";
        declineChoice.text = "Odrzuć propozycję";
        declineChoice.cost = 0.0f;
        
        event->addChoice(declineChoice);
    }
    
    return event;
}

} // namespace EventFactory