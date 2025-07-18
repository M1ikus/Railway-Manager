#ifndef PERSONNEL_H
#define PERSONNEL_H

#include <string>
#include <vector>
#include <chrono>

enum class PersonnelRole {
    DRIVER,           // Maszynista
    CONDUCTOR,        // Konduktor
    DISPATCHER,       // Dyspozytor
    STATION_MASTER,   // Zawiadowca stacji
    MECHANIC,         // Mechanik
    CLEANER,          // Sprzątacz
    MANAGER           // Kierownik
};

enum class PersonnelStatus {
    AVAILABLE,        // Dostępny
    ON_DUTY,          // Na służbie
    RESTING,          // Odpoczynek
    VACATION,         // Urlop
    SICK_LEAVE,       // Zwolnienie lekarskie
    TRAINING          // Szkolenie
};

struct WorkShift {
    std::chrono::system_clock::time_point startTime;
    std::chrono::system_clock::time_point endTime;
    std::string assignedTrainId;
    std::string assignedStationId;
    bool completed;
};

struct PersonnelStats {
    int totalShifts = 0;
    int totalHours = 0;
    int overtimeHours = 0;
    int sickDays = 0;
    int vacationDays = 0;
    float satisfaction = 1.0f;
    float performance = 1.0f;
};

class Personnel {
public:
    Personnel(const std::string& id, const std::string& firstName, 
              const std::string& lastName, PersonnelRole role);
    ~Personnel();
    
    // Podstawowe gettery
    const std::string& getId() const { return id; }
    const std::string& getFirstName() const { return firstName; }
    const std::string& getLastName() const { return lastName; }
    std::string getFullName() const { return firstName + " " + lastName; }
    PersonnelRole getRole() const { return role; }
    PersonnelStatus getStatus() const { return status; }
    
    // Podstawowe settery
    void setFirstName(const std::string& name) { firstName = name; }
    void setLastName(const std::string& name) { lastName = name; }
    void setRole(PersonnelRole r) { role = r; }
    void setStatus(PersonnelStatus s);
    
    // Dane osobowe
    int getAge() const { return age; }
    void setAge(int a) { age = a; }
    const std::string& getPhoneNumber() const { return phoneNumber; }
    void setPhoneNumber(const std::string& phone) { phoneNumber = phone; }
    const std::string& getAddress() const { return address; }
    void setAddress(const std::string& addr) { address = addr; }
    
    // Doświadczenie
    int getExperienceYears() const { return experienceYears; }
    void setExperienceYears(int years) { experienceYears = years; }
    int getSkillLevel() const { return skillLevel; }
    void setSkillLevel(int level) { skillLevel = std::max(1, std::min(10, level)); }
    void gainExperience(int months);
    
    // Stacja macierzysta
    const std::string& getHomeStationId() const { return homeStationId; }
    void setHomeStationId(const std::string& stationId) { homeStationId = stationId; }
    
    // Wynagrodzenie
    float getBaseSalary() const { return baseSalary; }
    void setBaseSalary(float salary) { baseSalary = salary; }
    float getHourlyRate() const { return hourlyRate; }
    void setHourlyRate(float rate) { hourlyRate = rate; }
    float getOvertimeRate() const { return overtimeRate; }
    void setOvertimeRate(float rate) { overtimeRate = rate; }
    float calculateMonthlySalary() const;
    
    // Harmonogram pracy
    void addShift(const WorkShift& shift);
    void removeShift(size_t index);
    const std::vector<WorkShift>& getShifts() const { return shifts; }
    WorkShift* getCurrentShift();
    std::vector<WorkShift> getShiftsInRange(
        const std::chrono::system_clock::time_point& start,
        const std::chrono::system_clock::time_point& end) const;
    
    // Czas pracy
    int getWorkingHoursThisWeek() const;
    int getWorkingHoursThisMonth() const;
    bool isOvertime() const;
    int getRestingHours() const;
    bool canWork() const;
    
    // Przypisania
    void assignToTrain(const std::string& trainId);
    void assignToStation(const std::string& stationId);
    void unassign();
    const std::string& getAssignedTrainId() const { return assignedTrainId; }
    const std::string& getAssignedStationId() const { return assignedStationId; }
    bool isAssigned() const { return !assignedTrainId.empty() || !assignedStationId.empty(); }
    
    // Urlopy i zwolnienia
    void startVacation(int days);
    void endVacation();
    void startSickLeave(int days);
    void endSickLeave();
    int getRemainingVacationDays() const { return remainingVacationDays; }
    bool isOnLeave() const;
    
    // Szkolenia
    void startTraining(const std::string& trainingType, int days);
    void completeTraining();
    const std::vector<std::string>& getCertifications() const { return certifications; }
    void addCertification(const std::string& cert) { certifications.push_back(cert); }
    bool hasCertification(const std::string& cert) const;
    
    // Zadowolenie i wydajność
    float getSatisfaction() const { return stats.satisfaction; }
    void setSatisfaction(float sat) { stats.satisfaction = std::max(0.0f, std::min(1.0f, sat)); }
    void changeSatisfaction(float delta) { setSatisfaction(stats.satisfaction + delta); }
    
    float getPerformance() const { return stats.performance; }
    void setPerformance(float perf) { stats.performance = std::max(0.0f, std::min(1.0f, perf)); }
    void updatePerformance();
    
    // Statystyki
    const PersonnelStats& getStats() const { return stats; }
    void updateStats(const PersonnelStats& newStats) { stats = newStats; }
    
    // Status
    bool isAvailable() const { return status == PersonnelStatus::AVAILABLE; }
    bool canDrive() const { return role == PersonnelRole::DRIVER && hasCertification("driving_license"); }
    
    // Operacje
    void clockIn();
    void clockOut();
    void takeBreak(int minutes);
    
private:
    // Podstawowe dane
    std::string id;
    std::string firstName;
    std::string lastName;
    PersonnelRole role;
    PersonnelStatus status;
    
    // Dane osobowe
    int age = 30;
    std::string phoneNumber;
    std::string address;
    
    // Doświadczenie
    int experienceYears = 0;
    int skillLevel = 5; // 1-10
    std::vector<std::string> certifications;
    
    // Praca
    std::string homeStationId;
    std::string assignedTrainId;
    std::string assignedStationId;
    std::vector<WorkShift> shifts;
    
    // Wynagrodzenie
    float baseSalary = 3000.0f;
    float hourlyRate = 20.0f;
    float overtimeRate = 30.0f;
    
    // Urlopy
    int totalVacationDays = 26;
    int remainingVacationDays = 26;
    std::chrono::system_clock::time_point vacationStart;
    std::chrono::system_clock::time_point vacationEnd;
    
    // Zwolnienia
    std::chrono::system_clock::time_point sickLeaveStart;
    std::chrono::system_clock::time_point sickLeaveEnd;
    
    // Szkolenia
    std::string currentTraining;
    std::chrono::system_clock::time_point trainingStart;
    std::chrono::system_clock::time_point trainingEnd;
    
    // Statystyki
    PersonnelStats stats;
    
    // Czas pracy
    std::chrono::system_clock::time_point lastClockIn;
    std::chrono::system_clock::time_point lastClockOut;
};

#endif // PERSONNEL_H