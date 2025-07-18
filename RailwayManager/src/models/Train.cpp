#include "Train.h"
#include "utils/Logger.h"
#include <algorithm>
#include <numeric>

Train::Train(const std::string& id, const std::string& name)
    : id(id), name(name), type(TrainType::PASSENGER_LOCAL), 
      status(TrainStatus::AVAILABLE) {
    purchaseDate = std::chrono::system_clock::now();
}

Train::~Train() {
    // Destruktor
}

void Train::setStatus(TrainStatus newStatus) {
    TrainStatus oldStatus = status;
    status = newStatus;
    
    LOG_INFO("Pociąg " + name + " zmienił status z " + 
             std::to_string(static_cast<int>(oldStatus)) + " na " + 
             std::to_string(static_cast<int>(newStatus)));
}

void Train::addUnit(const TrainUnit& unit) {
    // Sprawdź czy jednostka o tym ID już istnieje
    auto it = std::find_if(units.begin(), units.end(),
        [&unit](const TrainUnit& u) { return u.id == unit.id; });
    
    if (it != units.end()) {
        LOG_WARNING("Jednostka " + unit.id + " już jest w składzie pociągu " + name);
        return;
    }
    
    units.push_back(unit);
    LOG_INFO("Dodano jednostkę " + unit.series + "-" + unit.number + " do pociągu " + name);
}

void Train::removeUnit(const std::string& unitId) {
    auto it = std::find_if(units.begin(), units.end(),
        [&unitId](const TrainUnit& u) { return u.id == unitId; });
    
    if (it != units.end()) {
        LOG_INFO("Usunięto jednostkę " + it->id + " z pociągu " + name);
        units.erase(it);
    }
}

TrainUnit* Train::getUnit(const std::string& unitId) {
    auto it = std::find_if(units.begin(), units.end(),
        [&unitId](const TrainUnit& u) { return u.id == unitId; });
    
    return (it != units.end()) ? &(*it) : nullptr;
}

int Train::getTotalSeats() const {
    return std::accumulate(units.begin(), units.end(), 0,
        [](int sum, const TrainUnit& unit) { return sum + unit.seats; });
}

int Train::getTotalStandingRoom() const {
    return std::accumulate(units.begin(), units.end(), 0,
        [](int sum, const TrainUnit& unit) { return sum + unit.standingRoom; });
}

float Train::getTotalLength() const {
    return std::accumulate(units.begin(), units.end(), 0.0f,
        [](float sum, const TrainUnit& unit) { return sum + unit.length; });
}

float Train::getTotalWeight() const {
    return std::accumulate(units.begin(), units.end(), 0.0f,
        [](float sum, const TrainUnit& unit) { return sum + unit.weight; });
}

float Train::getMaxSpeed() const {
    if (units.empty()) return 0.0f;
    
    // Prędkość maksymalna to najmniejsza z prędkości jednostek
    float minSpeed = units[0].maxSpeed;
    for (const auto& unit : units) {
        minSpeed = std::min(minSpeed, unit.maxSpeed);
    }
    return minSpeed;
}

float Train::getTotalPower() const {
    return std::accumulate(units.begin(), units.end(), 0.0f,
        [](float sum, const TrainUnit& unit) { 
            return sum + (unit.hasEngine ? unit.power : 0.0f); 
        });
}

bool Train::isElectric() const {
    // Pociąg jest elektryczny jeśli wszystkie jednostki napędowe są elektryczne
    for (const auto& unit : units) {
        if (unit.hasEngine && !unit.isElectric) {
            return false;
        }
    }
    return true;
}

void Train::setCurrentPassengers(int count) {
    currentPassengers = std::max(0, std::min(count, getTotalCapacity()));
}

void Train::boardPassengers(int count) {
    int availableSpace = getTotalCapacity() - currentPassengers;
    int actualBoarding = std::min(count, availableSpace);
    currentPassengers += actualBoarding;
    
    if (actualBoarding < count) {
        LOG_WARNING("Pociąg " + name + " - brak miejsca dla " + 
                   std::to_string(count - actualBoarding) + " pasażerów");
    }
}

void Train::alightPassengers(int count) {
    currentPassengers = std::max(0, currentPassengers - count);
}

float Train::getOccupancyRate() const {
    int capacity = getTotalCapacity();
    return (capacity > 0) ? static_cast<float>(currentPassengers) / capacity : 0.0f;
}

void Train::setCurrentPosition(double lat, double lon) {
    currentLat = lat;
    currentLon = lon;
}

void Train::deteriorate(float amount) {
    condition = std::max(0.0f, condition - amount);
    
    // Brudzi się również
    cleanliness = std::max(0.0f, cleanliness - amount * 0.5f);
    
    // Sprawdź czy pociąg się zepsuł
    if (condition < 0.1f && status != TrainStatus::BROKEN) {
        setStatus(TrainStatus::BROKEN);
        LOG_ERROR("Pociąg " + name + " uległ awarii!");
    }
}

void Train::addKilometers(float km) {
    totalKm += km;
    kmSinceLastMaintenance += km;
    
    // Zużycie na podstawie przejechanych kilometrów
    deteriorate(km * 0.00001f); // 0.001% na kilometr
    
    // Zużycie paliwa (dla spalinowych)
    if (!isElectric()) {
        consumeFuel(km * 0.0002f); // 0.02% na kilometr
    }
}

bool Train::hasRequiredCrew() const {
    // Maszynista jest zawsze wymagany
    if (assignedDriverId.empty()) {
        return false;
    }
    
    // Konduktor wymagany dla pociągów pasażerskich
    if (type != TrainType::FREIGHT && type != TrainType::MAINTENANCE) {
        return !assignedConductorId.empty();
    }
    
    return true;
}

void Train::addMaintenanceRecord(const MaintenanceRecord& record) {
    maintenanceHistory.push_back(record);
    
    // Sortuj według daty
    std::sort(maintenanceHistory.begin(), maintenanceHistory.end(),
        [](const MaintenanceRecord& a, const MaintenanceRecord& b) {
            return a.date < b.date;
        });
}

float Train::getCurrentValue() const {
    // Deprecjacja liniowa - 5% rocznie
    auto now = std::chrono::system_clock::now();
    auto age = std::chrono::duration_cast<std::chrono::hours>(now - purchaseDate).count() / 8760.0f;
    float depreciation = 0.05f * age;
    
    // Dodatkowo uwzględnij stan techniczny
    float conditionFactor = 0.5f + condition * 0.5f;
    
    return purchasePrice * (1.0f - depreciation) * conditionFactor;
}

float Train::getDailyOperatingCost() const {
    float baseCost = 0.0f;
    
    // Koszt podstawowy zależny od typu
    switch (type) {
        case TrainType::PASSENGER_EXPRESS:
            baseCost = 5000.0f;
            break;
        case TrainType::PASSENGER_INTERCITY:
            baseCost = 3500.0f;
            break;
        case TrainType::PASSENGER_FAST:
            baseCost = 2500.0f;
            break;
        case TrainType::PASSENGER_REGIONAL:
            baseCost = 1800.0f;
            break;
        case TrainType::PASSENGER_LOCAL:
            baseCost = 1200.0f;
            break;
        case TrainType::FREIGHT:
            baseCost = 2000.0f;
            break;
        case TrainType::MAINTENANCE:
            baseCost = 800.0f;
            break;
    }
    
    // Modyfikator dla liczby jednostek
    baseCost *= (1.0f + units.size() * 0.2f);
    
    // Modyfikator dla stanu technicznego
    if (condition < 0.5f) {
        baseCost *= 1.5f; // 50% więcej gdy w złym stanie
    }
    
    // Koszty paliwa/energii
    if (isElectric()) {
        baseCost += getTotalPower() * 0.1f; // 0.1 PLN/kW
    } else {
        baseCost += getTotalPower() * 0.15f; // Diesel droższy
    }
    
    return baseCost;
}

float Train::getMaintenanceCost() const {
    float baseCost = purchasePrice * 0.02f; // 2% wartości zakupu
    
    // Modyfikator wieku
    auto now = std::chrono::system_clock::now();
    auto age = std::chrono::duration_cast<std::chrono::hours>(now - purchaseDate).count() / 8760.0f;
    float ageFactor = 1.0f + age * 0.1f; // +10% za każdy rok
    
    // Modyfikator stanu
    float conditionFactor = 2.0f - condition; // 1.0 gdy idealny, 2.0 gdy zepsuty
    
    // Modyfikator przebiegu
    float kmFactor = 1.0f + (kmSinceLastMaintenance / 100000.0f); // +100% co 100k km
    
    return baseCost * ageFactor * conditionFactor * kmFactor;
}

bool Train::isOperational() const {
    return status != TrainStatus::BROKEN && 
           status != TrainStatus::MAINTENANCE &&
           condition > 0.1f;
}

bool Train::canDepart() const {
    return isOperational() &&
           hasRequiredCrew() &&
           (isElectric() || fuelLevel > 0.1f) &&
           !assignedTimetableId.empty();
}