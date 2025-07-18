#include <QApplication>
#include <QMessageBox>
#include <SDL2/SDL.h>
#include <iostream>
#include <memory>

#include "core/Game.h"
#include "ui/MainWindow.h"
#include "utils/Logger.h"
#include "utils/Config.h"

int main(int argc, char *argv[])
{
    // Inicjalizacja loggera
    Logger::getInstance().init("railway_manager.log");
    Logger::getInstance().log(LogLevel::INFO, "=== Railway Manager Start ===");
    
    try {
        // Inicjalizacja Qt
        QApplication app(argc, argv);
        
        // Ustawienia aplikacji
        QApplication::setApplicationName("Railway Manager");
        QApplication::setApplicationDisplayName("Railway Manager");
        QApplication::setOrganizationName("RailwayManagerTeam");
        QApplication::setOrganizationDomain("railwaymanager.com");
        
        // Wczytaj konfigurację
        Config& config = Config::getInstance();
        config.load("config.json");
        
        // Inicjalizacja SDL2 dla renderowania mapy
        if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_TIMER) != 0) {
            throw std::runtime_error("Nie można zainicjalizować SDL2: " + std::string(SDL_GetError()));
        }
        
        // Inicjalizacja SDL_image
        int imgFlags = IMG_INIT_PNG | IMG_INIT_JPG;
        if (!(IMG_Init(imgFlags) & imgFlags)) {
            throw std::runtime_error("Nie można zainicjalizować SDL_image: " + std::string(IMG_GetError()));
        }
        
        // Inicjalizacja SDL_ttf
        if (TTF_Init() == -1) {
            throw std::runtime_error("Nie można zainicjalizować SDL_ttf: " + std::string(TTF_GetError()));
        }
        
        Logger::getInstance().log(LogLevel::INFO, "SDL2 zainicjalizowane pomyślnie");
        
        // Stwórz instancję gry
        auto game = std::make_shared<Game>();
        
        // Inicjalizuj grę
        if (!game->initialize()) {
            throw std::runtime_error("Nie można zainicjalizować gry");
        }
        
        Logger::getInstance().log(LogLevel::INFO, "Gra zainicjalizowana pomyślnie");
        
        // Stwórz główne okno
        MainWindow window(game);
        window.show();
        
        // Sprawdź czy to pierwsze uruchomienie
        if (config.getBool("first_run", true)) {
            QMessageBox::information(&window, 
                "Witaj w Railway Manager!", 
                "Witaj w Railway Manager!\n\n"
                "To symulator zarządzania koleją, gdzie będziesz:\n"
                "• Zarządzać taborem kolejowym\n"
                "• Tworzyć rozkłady jazdy\n"
                "• Dbać o zadowolenie pasażerów\n"
                "• Rozwijać swoją firmę kolejową\n\n"
                "Powodzenia!");
            
            config.setBool("first_run", false);
            config.save();
        }
        
        // Uruchom główną pętlę Qt
        int result = app.exec();
        
        // Cleanup
        game->shutdown();
        
        TTF_Quit();
        IMG_Quit();
        SDL_Quit();
        
        Logger::getInstance().log(LogLevel::INFO, "=== Railway Manager Stop ===");
        
        return result;
        
    } catch (const std::exception& e) {
        Logger::getInstance().log(LogLevel::ERROR, std::string("Krytyczny błąd: ") + e.what());
        
        // Pokaż błąd użytkownikowi
        QMessageBox::critical(nullptr, "Błąd krytyczny", 
            QString("Wystąpił błąd krytyczny:\n%1\n\nAplikacja zostanie zamknięta.")
            .arg(e.what()));
        
        return 1;
    }
}