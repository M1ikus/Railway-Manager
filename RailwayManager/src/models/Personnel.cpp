#include "Personnel.h"
#include "utils/Logger.h"
#include <algorithm>
#include <numeric>

Personnel::Personnel(const std::string& id, const std::string& firstName, 
                   const std::string& lastName, PersonnelRole role)
    : id(id), firstName(firstName), lastName(lastName), role(role), 
      status(PersonnelStatus::AVAILABLE) {
    
    // Ustaw domyślne wynagrodzenie w zależności od roli
    switch (role) {
        case PersonnelRole::DRIVER:
            baseSalary = 5000.0f;
            hourlyRate = 35.0f;
            overtimeRate = 52.5f;
            break;
        case PersonnelRole::CONDUCTOR:
            baseSalary = 3500.0f;
            hourlyRate = 25.0f;
            overtimeRate = 37.5f;
            break;
        case PersonnelRole::DISPATCHER:
            baseSalary = 4500.0f;
            hourlyRate = 30.0f;
            overtimeRate = 45.0f;
            break;
        case PersonnelRole::STATION_MASTER:
            baseSalary = 6000.0f;
            hourlyRate = 40.0f;
            overtimeRate = 60.0f;
            break;
        case PersonnelRole::MECHANIC:
            baseSalary = 4000.0f;
            hourlyRate = 28.0f;
            overtimeRate = 42.0f;
            break;
        case PersonnelRole::CLEANER:
            baseSalary = 2800.0f;
            hourlyRate = 20.0f;
            overtimeRate = 30.0f;
            break;
        case PersonnelRole::MANAGER:
            baseSalary = 7000.0f;
            hourlyRate = 45.0f;
            overtimeRate = 67.5f;
            break;
    }
}

Personnel::~Personnel() {
}

void Personnel::setStatus(PersonnelStatus s) {
    PersonnelStatus oldStatus = status;
    status = s;
    
    LOG_INFO("Pracownik " + getFullName() + " zmienił status z " +
             std::to_string(static_cast<int>(oldStatus)) + " na " +
             std::to_string(static_cast<int>(s)));
}

void Personnel::gainExperience(int months) {
    int totalMonths = experienceYears * 12 + months;
    experienceYears = totalMonths / 12;
    
    // Automatycznie zwiększ poziom umiejętności
    int expectedSkill = std::min(10, 3 + experienceYears / 2);
    if (skillLevel < expectedSkill) {
        skillLevel = expectedSkill;
        LOG_INFO("Pracownik " + getFullName() + " zwiększył poziom umiejętności do " +
                 std::to_string(skillLevel));
    }
}

float Personnel::calculateMonthlySalary() const {
    float salary = baseSalary;
    
    // Dodatek za doświadczenie (2% za każdy rok)
    salary *= (1.0f + experienceYears * 0.02f);
    
    // Dodatek za umiejętności
    salary *= (0.8f + skillLevel * 0.04f); // 80% - 120%
    
    // Dodatek za nadgodziny
    int overtimeHours = std::max(0, getWorkingHoursThisMonth() - 160);
    salary += overtimeHours * overtimeRate;
    
    return salary;
}

void Personnel::addShift(const WorkShift& shift) {
    shifts.push_back(shift);
    stats.totalShifts++;
    
    // Oblicz godziny
    auto duration = shift.endTime - shift.startTime;
    int hours = std::chrono::duration_cast<std::chrono::hours>(duration).count();
    stats.totalHours += hours;
    
    // Sprawdź nadgodziny
    if (hours > 8) {
        stats.overtimeHours += hours - 8;
    }
}

void Personnel::removeShift(size_t index) {
    if (index < shifts.size()) {
        shifts.erase(shifts.begin() + index);
    }
}

WorkShift* Personnel::getCurrentShift() {
    auto now = std::chrono::system_clock::now();
    
    for (auto& shift : shifts) {
        if (shift.startTime <= now && shift.endTime >= now) {
            return &shift;
        }
    }
    
    return nullptr;
}

std::vector<WorkShift> Personnel::getShiftsInRange(
    const std::chrono::system_clock::time_point& start,
    const std::chrono::system_clock::time_point& end) const {
    
    std::vector<WorkShift> result;
    
    for (const auto& shift : shifts) {
        if (shift.startTime >= start && shift.endTime <= end) {
            result.push_back(shift);
        }
    }
    
    return result;
}

int Personnel::getWorkingHoursThisWeek() const {
    auto now = std::chrono::system_clock::now();
    auto weekAgo = now - std::chrono::hours(24 * 7);
    
    auto weekShifts = getShiftsInRange(weekAgo, now);
    
    int totalHours = 0;
    for (const auto& shift : weekShifts) {
        auto duration = shift.endTime - shift.startTime;
        totalHours += std::chrono::duration_cast<std::chrono::hours>(duration).count();
    }
    
    return totalHours;
}

int Personnel::getWorkingHoursThisMonth() const {
    auto now = std::chrono::system_clock::now();
    auto monthAgo = now - std::chrono::hours(24 * 30);
    
    auto monthShifts = getShiftsInRange(monthAgo, now);
    
    int totalHours = 0;
    for (const auto& shift : monthShifts) {
        auto duration = shift.endTime - shift.startTime;
        totalHours += std::chrono::duration_cast<std::chrono::hours>(duration).count();
    }
    
    return totalHours;
}

bool Personnel::isOvertime() const {
    return getWorkingHoursThisWeek() > 40;
}

int Personnel::getRestingHours() const {
    if (shifts.empty()) {
        return 24; // Nie ma zmian, pełny odpoczynek
    }
    
    // Znajdź ostatnią zakończoną zmianę
    auto now = std::chrono::system_clock::now();
    std::chrono::system_clock::time_point lastEnd;
    bool found = false;
    
    for (const auto& shift : shifts) {
        if (shift.endTime < now && (!found || shift.endTime > lastEnd)) {
            lastEnd = shift.endTime;
            found = true;
        }
    }
    
    if (!found) {
        return 24;
    }
    
    auto restDuration = now - lastEnd;
    return std::chrono::duration_cast<std::chrono::hours>(restDuration).count();
}

bool Personnel::canWork() const {
    // Sprawdź status
    if (status != PersonnelStatus::AVAILABLE && status != PersonnelStatus::ON_DUTY) {
        return false;
    }
    
    // Sprawdź odpoczynek (minimum 11 godzin)
    if (getRestingHours() < 11) {
        return false;
    }
    
    // Sprawdź tygodniowy limit godzin
    if (getWorkingHoursThisWeek() >= 48) {
        return false;
    }
    
    // Sprawdź certyfikaty dla maszynisty
    if (role == PersonnelRole::DRIVER && !hasCertification("driving_license")) {
        return false;
    }
    
    return true;
}

void Personnel::assignToTrain(const std::string& trainId) {
    assignedTrainId = trainId;
    if (status == PersonnelStatus::AVAILABLE) {
        status = PersonnelStatus::ON_DUTY;
    }
}

void Personnel::assignToStation(const std::string& stationId) {
    assignedStationId = stationId;
    if (status == PersonnelStatus::AVAILABLE) {
        status = PersonnelStatus::ON_DUTY;
    }
}

void Personnel::unassign() {
    assignedTrainId.clear();
    assignedStationId.clear();
    if (status == PersonnelStatus::ON_DUTY) {
        status = PersonnelStatus::AVAILABLE;
    }
}

void Personnel::startVacation(int days) {
    if (days > remainingVacationDays) {
        LOG_WARNING("Pracownik " + getFullName() + " nie ma wystarczająco dni urlopu");
        return;
    }
    
    vacationStart = std::chrono::system_clock::now();
    vacationEnd = vacationStart + std::chrono::hours(24 * days);
    remainingVacationDays -= days;
    status = PersonnelStatus::VACATION;
    
    LOG_INFO("Pracownik " + getFullName() + " rozpoczął urlop na " + 
             std::to_string(days) + " dni");
}

void Personnel::endVacation() {
    vacationStart = std::chrono::system_clock::time_point();
    vacationEnd = std::chrono::system_clock::time_point();
    status = PersonnelStatus::AVAILABLE;
    
    LOG_INFO("Pracownik " + getFullName() + " wrócił z urlopu");
}

void Personnel::startSickLeave(int days) {
    sickLeaveStart = std::chrono::system_clock::now();
    sickLeaveEnd = sickLeaveStart + std::chrono::hours(24 * days);
    status = PersonnelStatus::SICK_LEAVE;
    stats.sickDays += days;
    
    LOG_INFO("Pracownik " + getFullName() + " jest na zwolnieniu lekarskim przez " + 
             std::to_string(days) + " dni");
}

void Personnel::endSickLeave() {
    sickLeaveStart = std::chrono::system_clock::time_point();
    sickLeaveEnd = std::chrono::system_clock::time_point();
    status = PersonnelStatus::AVAILABLE;
    
    LOG_INFO("Pracownik " + getFullName() + " wrócił ze zwolnienia lekarskiego");
}

bool Personnel::isOnLeave() const {
    return status == PersonnelStatus::VACATION || status == PersonnelStatus::SICK_LEAVE;
}

void Personnel::startTraining(const std::string& trainingType, int days) {
    currentTraining = trainingType;
    trainingStart = std::chrono::system_clock::now();
    trainingEnd = trainingStart + std::chrono::hours(24 * days);
    status = PersonnelStatus::TRAINING;
    
    LOG_INFO("Pracownik " + getFullName() + " rozpoczął szkolenie: " + trainingType);
}

void Personnel::completeTraining() {
    if (!currentTraining.empty()) {
        addCertification(currentTraining);
        
        // Zwiększ umiejętności
        if (skillLevel < 10) {
            skillLevel++;
        }
        
        LOG_INFO("Pracownik " + getFullName() + " ukończył szkolenie: " + currentTraining);
    }
    
    currentTraining.clear();
    trainingStart = std::chrono::system_clock::time_point();
    trainingEnd = std::chrono::system_clock::time_point();
    status = PersonnelStatus::AVAILABLE;
}

bool Personnel::hasCertification(const std::string& cert) const {
    return std::find(certifications.begin(), certifications.end(), cert) != certifications.end();
}

void Personnel::updatePerformance() {
    // Bazowa wydajność zależy od umiejętności
    float basePerformance = 0.5f + skillLevel * 0.05f;
    
    // Modyfikatory
    float satisfactionModifier = 0.5f + stats.satisfaction * 0.5f;
    float fatigueModifier = 1.0f;
    
    // Zmęczenie - spadek wydajności przy nadgodzinach
    if (isOvertime()) {
        int overtimeHours = getWorkingHoursThisWeek() - 40;
        fatigueModifier = std::max(0.5f, 1.0f - overtimeHours * 0.05f);
    }
    
    stats.performance = basePerformance * satisfactionModifier * fatigueModifier;
}

void Personnel::clockIn() {
    if (status != PersonnelStatus::AVAILABLE) {
        LOG_WARNING("Pracownik " + getFullName() + " nie może rozpocząć pracy - status: " +
                   std::to_string(static_cast<int>(status)));
        return;
    }
    
    lastClockIn = std::chrono::system_clock::now();
    status = PersonnelStatus::ON_DUTY;
    
    LOG_INFO("Pracownik " + getFullName() + " rozpoczął pracę");
}

void Personnel::clockOut() {
    if (status != PersonnelStatus::ON_DUTY) {
        LOG_WARNING("Pracownik " + getFullName() + " nie jest na służbie");
        return;
    }
    
    lastClockOut = std::chrono::system_clock::now();
    
    // Dodaj zmianę
    if (lastClockIn != std::chrono::system_clock::time_point()) {
        WorkShift shift;
        shift.startTime = lastClockIn;
        shift.endTime = lastClockOut;
        shift.assignedTrainId = assignedTrainId;
        shift.assignedStationId = assignedStationId;
        shift.completed = true;
        
        addShift(shift);
    }
    
    status = PersonnelStatus::RESTING;
    
    // Po odpoczynku przejdź do dostępnego
    // TODO: Zaplanuj automatyczne przejście do AVAILABLE po 11 godzinach
    
    LOG_INFO("Pracownik " + getFullName() + " zakończył pracę");
}

void Personnel::takeBreak(int minutes) {
    // TODO: Implementacja przerw
    LOG_INFO("Pracownik " + getFullName() + " robi przerwę na " + 
             std::to_string(minutes) + " minut");
}