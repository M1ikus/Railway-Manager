#include "Station.h"
#include "utils/Logger.h"
#include <algorithm>
#include <cmath>

Station::Station(const std::string& id, const std::string& name) 
    : id(id), name(name), type(StationType::LOCAL), size(StationSize::SMALL) {
    
    // Domyślne udogodnienia dla małej stacji
    facilities = {
        true,   // hasTicketOffice
        true,   // hasWaitingRoom
        false,  // hasRestaurant
        false,  // hasParking
        true,   // hasToilets
        false,  // hasBikeRacks
        false,  // hasElevators
        false,  // isAccessible
        0       // parkingSpaces
    };
}

Station::~Station() {
    // Destruktor
}

void Station::setCoordinates(double lat, double lon) {
    latitude = lat;
    longitude = lon;
}

void Station::addPlatform(const Platform& platform) {
    // Sprawdź czy peron o tym numerze już istnieje
    auto it = std::find_if(platforms.begin(), platforms.end(),
        [&platform](const Platform& p) { return p.number == platform.number; });
    
    if (it != platforms.end()) {
        LOG_WARNING("Peron nr " + std::to_string(platform.number) + 
                   " już istnieje na stacji " + name);
        return;
    }
    
    platforms.push_back(platform);
    
    // Sortuj perony według numeru
    std::sort(platforms.begin(), platforms.end(),
        [](const Platform& a, const Platform& b) { return a.number < b.number; });
}

void Station::removePlatform(int platformNumber) {
    platforms.erase(
        std::remove_if(platforms.begin(), platforms.end(),
            [platformNumber](const Platform& p) { return p.number == platformNumber; }),
        platforms.end()
    );
}

Platform* Station::getPlatform(int platformNumber) {
    auto it = std::find_if(platforms.begin(), platforms.end(),
        [platformNumber](const Platform& p) { return p.number == platformNumber; });
    
    return (it != platforms.end()) ? &(*it) : nullptr;
}

int Station::getAvailablePlatform(int requiredLength) const {
    for (const auto& platform : platforms) {
        if (!platform.occupied && platform.length >= requiredLength) {
            return platform.number;
        }
    }
    return -1; // Brak dostępnego peronu
}

void Station::occupyPlatform(int platformNumber, const std::string& trainId) {
    Platform* platform = getPlatform(platformNumber);
    if (platform) {
        platform->occupied = true;
        platform->trainId = trainId;
        stats.totalTrainsToday++;
    }
}

void Station::freePlatform(int platformNumber) {
    Platform* platform = getPlatform(platformNumber);
    if (platform) {
        platform->occupied = false;
        platform->trainId.clear();
    }
}

int Station::getCurrentTrains() const {
    int count = 0;
    for (const auto& platform : platforms) {
        if (platform.occupied) {
            count++;
        }
    }
    return count;
}

void Station::addPassengers(int count) {
    currentPassengers = std::min(currentPassengers + count, maxPassengers);
    stats.totalPassengersToday += count;
    stats.totalPassengersMonth += count;
    stats.totalPassengersYear += count;
}

void Station::removePassengers(int count) {
    currentPassengers = std::max(0, currentPassengers - count);
}

void Station::addConnection(const std::string& lineId) {
    if (!hasConnection(lineId)) {
        connectedLines.push_back(lineId);
    }
}

void Station::removeConnection(const std::string& lineId) {
    connectedLines.erase(
        std::remove(connectedLines.begin(), connectedLines.end(), lineId),
        connectedLines.end()
    );
}

bool Station::hasConnection(const std::string& lineId) const {
    return std::find(connectedLines.begin(), connectedLines.end(), lineId) 
           != connectedLines.end();
}

bool Station::canAcceptTrain(int trainLength) const {
    // Sprawdź czy jest wolny peron odpowiedniej długości
    return getAvailablePlatform(trainLength) != -1;
}

float Station::calculateTicketPrice(const Station* destination, const std::string& trainType) const {
    if (!destination) {
        return 0.0f;
    }
    
    // Oblicz odległość (uproszczone - prosta linia)
    const double R = 6371.0; // Promień Ziemi w km
    double lat1 = latitude * M_PI / 180.0;
    double lat2 = destination->latitude * M_PI / 180.0;
    double dLat = (destination->latitude - latitude) * M_PI / 180.0;
    double dLon = (destination->longitude - longitude) * M_PI / 180.0;
    
    double a = sin(dLat/2) * sin(dLat/2) +
               cos(lat1) * cos(lat2) *
               sin(dLon/2) * sin(dLon/2);
    double c = 2 * atan2(sqrt(a), sqrt(1-a));
    double distance = R * c;
    
    // Bazowa cena: 0.30 PLN/km
    float basePrice = distance * 0.30f;
    
    // Modyfikatory dla typu pociągu
    float multiplier = 1.0f;
    if (trainType == "express") {
        multiplier = 1.5f;
    } else if (trainType == "intercity") {
        multiplier = 1.3f;
    } else if (trainType == "regional") {
        multiplier = 0.9f;
    }
    
    // Modyfikator dla typu stacji
    if (type == StationType::MAJOR) {
        multiplier += 0.1f;
    }
    if (destination->type == StationType::MAJOR) {
        multiplier += 0.1f;
    }
    
    return basePrice * multiplier;
}