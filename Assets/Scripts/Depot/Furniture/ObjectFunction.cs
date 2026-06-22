namespace DepotSystem.Furniture
{
    /// <summary>
    /// Funkcje obiektu furniture — co umożliwia gracz postawiwszy go w pomieszczeniu.
    /// Obiekt może mieć wiele funkcji (np. krzesło = SeatingRest + SeatingMeal).
    /// </summary>
    public enum ObjectFunction
    {
        None,                    // dekoracja, brak funkcji
        WorkstationDispatcher,   // biurko dyspozytora
        WorkstationOffice,       // biurko biurowe (Personnel office)
        WorkstationSupervisor,   // biurko naczelnika
        WorkstationTraffic,      // pulpit dyżurnego ruchu
        ServicePit,              // kanał serwisowy (M-Modernization: slot warsztatowy)
        WashStation,             // brama+szczotki myjni (M-Modernization: myjnia slot)
        Refueling,               // MM-D14: tankowanie pojazdu (fuel_pump indoor + FuelStation outdoor)
        Painting,                // MM-12: stanowisko lakierowania (self-paint w Hall)
        WaterService,            // MM-17: wodowanie (uzupełnienie wody + opróżnienie zb. fekaliów)
        ToolStorage,             // szafa narzędziowa
        StorageGoods,            // regał magazynowy (dolicza do PartInventory per-depot)
        StoragePersonal,         // szafka pracownicza (locker)
        SeatingRest,             // sofa, fotel
        SeatingMeal,             // krzesło przy stole
        Kitchen,                 // kuchnia (lada+szafki)
        Sanitary,                // WC, prysznic, umywalka
        Passage,                 // drzwi (passable cell + animacja)
        Decoration               // tablica, roślina, lampa
    }
}
