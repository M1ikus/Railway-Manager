using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// System kontroli ruchu lokomotyw i wagonów w zajezdni
    /// Obsługuje spawning, pathfinding i animację pojazdów
    /// </summary>
    public class VehicleController : MonoBehaviour
    {
        [Header("Vehicle Prefabs")]
        public GameObject locomotivePrefab;
        public GameObject freightCarPrefab;
        public GameObject passengerCarPrefab;
        public GameObject tankCarPrefab;

        [Header("Movement Parameters")]
        [Tooltip("Prędkość poruszania się pojazdów (m/s)")]
        public float vehicleSpeed = 2.0f;

        [Tooltip("Przyspieszenie (m/s²)")]
        public float acceleration = 0.5f;

        [Tooltip("Opóźnienie (m/s²)")]
        public float deceleration = 1.0f;

        [Header("Vehicle Dimensions")]
        public Vector3 locomotiveSize = new Vector3(3f, 4f, 15f);
        public Vector3 carSize = new Vector3(2.8f, 3.5f, 12f);

        [Header("Materials")]
        public Material locomotiveMaterial;
        public Material freightCarMaterial;
        public Material passengerCarMaterial;

        [Header("Parent")]
        public Transform vehiclesParent;

        // Internal storage
        private List<GameObject> vehicleObjects = new();
        private Dictionary<int, VehicleUnit> vehicles = new();
        private int nextVehicleId = 0;

        void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (vehiclesParent == null)
            {
                vehiclesParent = new GameObject("Vehicles").transform;
                vehiclesParent.SetParent(transform);
            }
            if (locomotiveMaterial == null)
                CreateDefaultMaterials();
        }

        /// <summary>
        /// Tworzy domyślne materiały
        /// </summary>
        private void CreateDefaultMaterials()
        {
            if (locomotiveMaterial == null)
            {
                locomotiveMaterial = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(locomotiveMaterial, new Color(0.8f, 0.1f, 0.1f)); // Red
                MaterialFactory.SetMetallicSmoothness(locomotiveMaterial, 0.5f, 0.6f);
            }

            if (freightCarMaterial == null)
            {
                freightCarMaterial = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(freightCarMaterial, new Color(0.4f, 0.3f, 0.2f)); // Brown
                MaterialFactory.SetMetallicSmoothness(freightCarMaterial, 0.2f, 0.3f);
            }

            if (passengerCarMaterial == null)
            {
                passengerCarMaterial = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(passengerCarMaterial, new Color(0.1f, 0.3f, 0.8f)); // Blue
                MaterialFactory.SetMetallicSmoothness(passengerCarMaterial, 0.4f, 0.7f);
            }
        }

        /// <summary>
        /// Tworzy pojazd w określonym miejscu
        /// </summary>
        public VehicleUnit SpawnVehicle(VehicleType type, Vector3 position, Quaternion rotation = default)
        {
            EnsureInitialized();

            if (rotation == default)
                rotation = Quaternion.identity;

            GameObject vehicleObject = null;

            // Użyj prefabu jeśli dostępny, w przeciwnym razie wygeneruj proceduralnie
            switch (type)
            {
                case VehicleType.Locomotive:
                    vehicleObject = locomotivePrefab != null
                        ? Instantiate(locomotivePrefab, position, rotation)
                        : GenerateLocomotive(position, rotation);
                    break;

                case VehicleType.FreightCar:
                    vehicleObject = freightCarPrefab != null
                        ? Instantiate(freightCarPrefab, position, rotation)
                        : GenerateFreightCar(position, rotation);
                    break;

                case VehicleType.PassengerCar:
                    vehicleObject = passengerCarPrefab != null
                        ? Instantiate(passengerCarPrefab, position, rotation)
                        : GeneratePassengerCar(position, rotation);
                    break;

                case VehicleType.TankCar:
                    vehicleObject = tankCarPrefab != null
                        ? Instantiate(tankCarPrefab, position, rotation)
                        : GenerateTankCar(position, rotation);
                    break;
            }

            if (vehicleObject != null)
            {
                vehicleObject.transform.SetParent(vehiclesParent);
                vehicleObjects.Add(vehicleObject);

                VehicleUnit vehicle = new VehicleUnit
                {
                    Id = nextVehicleId++,
                    Type = type,
                    GameObject = vehicleObject,
                    Position = position,
                    CurrentSpeed = 0f,
                    TargetSpeed = 0f,
                    CurrentTrackId = -1,
                    IsMoving = false
                };

                vehicles[vehicle.Id] = vehicle;

                Log.Info($"[VehicleController] Spawned {type} with ID {vehicle.Id}");
                return vehicle;
            }

            return null;
        }

        /// <summary>
        /// Generuje proceduralnie lokomotywę
        /// </summary>
        private GameObject GenerateLocomotive(Vector3 position, Quaternion rotation)
        {
            GameObject locomotive = new GameObject("Locomotive");
            locomotive.transform.position = position;
            locomotive.transform.rotation = rotation;

            // Główny korpus
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(locomotive.transform, false);
            body.transform.localPosition = new Vector3(0, locomotiveSize.y / 2, 0);
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = locomotiveSize;
            body.GetComponent<MeshRenderer>().material = locomotiveMaterial;

            // Kabina maszynisty
            GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            cabin.transform.SetParent(locomotive.transform, false);
            cabin.transform.localPosition = new Vector3(0, locomotiveSize.y + 1, locomotiveSize.z / 4);
            cabin.transform.localRotation = Quaternion.identity;
            cabin.transform.localScale = new Vector3(locomotiveSize.x * 0.9f, 2f, locomotiveSize.z / 3);
            cabin.GetComponent<MeshRenderer>().material = locomotiveMaterial;

            // Koła (4 pary)
            float wheelRadius = 0.8f;
            float wheelWidth = 0.4f;
            float wheelSpacing = locomotiveSize.z / 5;

            for (int i = 0; i < 4; i++)
            {
                float zPos = -locomotiveSize.z / 2 + wheelSpacing * (i + 0.5f);
                GenerateWheelPair(locomotive, new Vector3(0, wheelRadius, zPos), wheelRadius, wheelWidth);
            }

            return locomotive;
        }

        /// <summary>
        /// Generuje proceduralnie wagon towarowy
        /// </summary>
        private GameObject GenerateFreightCar(Vector3 position, Quaternion rotation)
        {
            GameObject car = new GameObject("FreightCar");
            car.transform.position = position;
            car.transform.rotation = rotation;

            // Główny korpus (otwarty)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(car.transform, false);
            body.transform.localPosition = new Vector3(0, carSize.y / 2, 0);
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = carSize;
            body.GetComponent<MeshRenderer>().material = freightCarMaterial;

            // Koła (2 pary)
            float wheelRadius = 0.7f;
            float wheelWidth = 0.4f;

            GenerateWheelPair(car, new Vector3(0, wheelRadius, -carSize.z / 3), wheelRadius, wheelWidth);
            GenerateWheelPair(car, new Vector3(0, wheelRadius, carSize.z / 3), wheelRadius, wheelWidth);

            return car;
        }

        /// <summary>
        /// Generuje proceduralnie wagon pasażerski
        /// </summary>
        private GameObject GeneratePassengerCar(Vector3 position, Quaternion rotation)
        {
            GameObject car = new GameObject("PassengerCar");
            car.transform.position = position;
            car.transform.rotation = rotation;

            // Główny korpus
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(car.transform, false);
            body.transform.localPosition = new Vector3(0, carSize.y / 2, 0);
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = carSize;
            body.GetComponent<MeshRenderer>().material = passengerCarMaterial;

            // Okna (prosty efekt)
            GenerateCarWindows(car, carSize, 8);

            // Koła (2 pary)
            float wheelRadius = 0.7f;
            float wheelWidth = 0.4f;

            GenerateWheelPair(car, new Vector3(0, wheelRadius, -carSize.z / 3), wheelRadius, wheelWidth);
            GenerateWheelPair(car, new Vector3(0, wheelRadius, carSize.z / 3), wheelRadius, wheelWidth);

            return car;
        }

        /// <summary>
        /// Generuje proceduralnie wagon cysternę
        /// </summary>
        private GameObject GenerateTankCar(Vector3 position, Quaternion rotation)
        {
            GameObject car = new GameObject("TankCar");
            car.transform.position = position;
            car.transform.rotation = rotation;

            // Rama
            GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(car.transform, false);
            frame.transform.localPosition = new Vector3(0, 1f, 0);
            frame.transform.localRotation = Quaternion.identity;
            frame.transform.localScale = new Vector3(2.5f, 0.3f, carSize.z);
            frame.GetComponent<MeshRenderer>().material = freightCarMaterial;

            // Zbiornik (cylinder)
            GameObject tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tank.name = "Tank";
            tank.transform.SetParent(car.transform, false);
            tank.transform.localPosition = new Vector3(0, 2f, 0);
            tank.transform.localRotation = Quaternion.Euler(90, 0, 0);
            tank.transform.localScale = new Vector3(2f, carSize.z / 2, 2f);
            tank.GetComponent<MeshRenderer>().material = locomotiveMaterial;

            // Koła (2 pary)
            float wheelRadius = 0.7f;
            float wheelWidth = 0.4f;

            GenerateWheelPair(car, new Vector3(0, wheelRadius, -carSize.z / 3), wheelRadius, wheelWidth);
            GenerateWheelPair(car, new Vector3(0, wheelRadius, carSize.z / 3), wheelRadius, wheelWidth);

            return car;
        }

        /// <summary>
        /// Generuje parę kół
        /// </summary>
        private void GenerateWheelPair(GameObject parent, Vector3 position, float radius, float width)
        {
            Material wheelMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(wheelMaterial, new Color(0.1f, 0.1f, 0.1f));
            wheelMaterial.SetFloat("_Metallic", 0.8f);

            // Lewe koło
            GameObject leftWheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leftWheel.name = "LeftWheel";
            leftWheel.transform.SetParent(parent.transform, false);
            leftWheel.transform.localPosition = position + new Vector3(-1.2f, 0, 0);
            leftWheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            leftWheel.transform.localScale = new Vector3(radius * 2, width / 2, radius * 2);
            leftWheel.GetComponent<MeshRenderer>().material = wheelMaterial;
            Destroy(leftWheel.GetComponent<Collider>());

            // Prawe koło
            GameObject rightWheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rightWheel.name = "RightWheel";
            rightWheel.transform.SetParent(parent.transform, false);
            rightWheel.transform.localPosition = position + new Vector3(1.2f, 0, 0);
            rightWheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
            rightWheel.transform.localScale = new Vector3(radius * 2, width / 2, radius * 2);
            rightWheel.GetComponent<MeshRenderer>().material = wheelMaterial;
            Destroy(rightWheel.GetComponent<Collider>());
        }

        /// <summary>
        /// Generuje okna dla wagonu
        /// </summary>
        private void GenerateCarWindows(GameObject parent, Vector3 carSize, int windowCount)
        {
            Material windowMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(windowMaterial, new Color(0.6f, 0.8f, 1f, 0.3f));

            float windowSpacing = carSize.z / (windowCount + 1);
            float windowWidth = 0.8f;
            float windowHeight = 1.5f;

            for (int i = 0; i < windowCount; i++)
            {
                float zPos = -carSize.z / 2 + windowSpacing * (i + 1);

                // Lewe okno
                GameObject leftWindow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leftWindow.name = $"LeftWindow_{i}";
                leftWindow.transform.SetParent(parent.transform, false);
                leftWindow.transform.localPosition = new Vector3(-carSize.x / 2 - 0.05f, carSize.y * 0.6f, zPos);
                leftWindow.transform.localRotation = Quaternion.identity;
                leftWindow.transform.localScale = new Vector3(0.1f, windowHeight, windowWidth);
                leftWindow.GetComponent<MeshRenderer>().material = windowMaterial;
                Destroy(leftWindow.GetComponent<Collider>());

                // Prawe okno
                GameObject rightWindow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rightWindow.name = $"RightWindow_{i}";
                rightWindow.transform.SetParent(parent.transform, false);
                rightWindow.transform.localPosition = new Vector3(carSize.x / 2 + 0.05f, carSize.y * 0.6f, zPos);
                rightWindow.transform.localRotation = Quaternion.identity;
                rightWindow.transform.localScale = new Vector3(0.1f, windowHeight, windowWidth);
                rightWindow.GetComponent<MeshRenderer>().material = windowMaterial;
                Destroy(rightWindow.GetComponent<Collider>());
            }
        }

        /// <summary>
        /// Przesuwa pojazd do określonej pozycji
        /// </summary>
        public void MoveVehicleToPosition(int vehicleId, Vector3 targetPosition, float duration = 5f)
        {
            if (!vehicles.ContainsKey(vehicleId))
            {
                Log.Warn($"[VehicleController] Vehicle {vehicleId} not found!");
                return;
            }

            VehicleUnit vehicle = vehicles[vehicleId];
            StartCoroutine(MoveVehicleCoroutine(vehicle, targetPosition, duration));
        }

        /// <summary>
        /// Korutyna poruszająca pojazdem
        /// </summary>
        private IEnumerator MoveVehicleCoroutine(VehicleUnit vehicle, Vector3 targetPosition, float duration)
        {
            vehicle.IsMoving = true;
            Vector3 startPosition = vehicle.GameObject.transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Płynna interpolacja z ease-in-out
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                vehicle.GameObject.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);

                // Oblicz prędkość
                vehicle.CurrentSpeed = Vector3.Distance(startPosition, targetPosition) / duration;

                yield return null;
            }

            vehicle.GameObject.transform.position = targetPosition;
            vehicle.Position = targetPosition;
            vehicle.CurrentSpeed = 0f;
            vehicle.IsMoving = false;

            Log.Info($"[VehicleController] Vehicle {vehicle.Id} reached target position");
        }

        /// <summary>
        /// Tworzy skład pociągu (lokomotywa + wagony)
        /// </summary>
        public List<VehicleUnit> CreateTrain(Vector3 position, int locomotiveCount, int carCount, VehicleType carType)
        {
            List<VehicleUnit> train = new List<VehicleUnit>();
            float spacing = 15f; // Odstęp między pojazdami

            // Dodaj lokomotywy
            for (int i = 0; i < locomotiveCount; i++)
            {
                Vector3 locoPos = position + new Vector3(0, 0, i * spacing);
                VehicleUnit loco = SpawnVehicle(VehicleType.Locomotive, locoPos);
                train.Add(loco);
            }

            // Dodaj wagony
            for (int i = 0; i < carCount; i++)
            {
                Vector3 carPos = position + new Vector3(0, 0, (locomotiveCount + i) * spacing);
                VehicleUnit car = SpawnVehicle(carType, carPos);
                train.Add(car);
            }

            Log.Info($"[VehicleController] Created train with {locomotiveCount} locomotives and {carCount} cars");
            return train;
        }

        /// <summary>
        /// Usuwa pojazd
        /// </summary>
        public void RemoveVehicle(int vehicleId)
        {
            if (vehicles.ContainsKey(vehicleId))
            {
                VehicleUnit vehicle = vehicles[vehicleId];
                if (vehicle.GameObject != null)
                {
                    vehicleObjects.Remove(vehicle.GameObject);
                    Destroy(vehicle.GameObject);
                }
                vehicles.Remove(vehicleId);
            }
        }

        /// <summary>
        /// Czyści wszystkie pojazdy
        /// </summary>
        public void ClearVehicles()
        {
            foreach (var vehicleObject in vehicleObjects)
            {
                if (vehicleObject != null)
                    Destroy(vehicleObject);
            }

            vehicleObjects.Clear();
            vehicles.Clear();
            nextVehicleId = 0;
        }

        /// <summary>
        /// Pobiera pojazd po ID
        /// </summary>
        public VehicleUnit GetVehicle(int vehicleId)
        {
            return vehicles.ContainsKey(vehicleId) ? vehicles[vehicleId] : null;
        }
    }

    /// <summary>
    /// Reprezentuje pojedynczy pojazd (lokomotywę lub wagon)
    /// </summary>
    [System.Serializable]
    public class VehicleUnit
    {
        public int Id;
        public VehicleType Type;
        public GameObject GameObject;
        public Vector3 Position;
        public float CurrentSpeed;
        public float TargetSpeed;
        public int CurrentTrackId;
        public bool IsMoving;
    }

    /// <summary>
    /// Typy pojazdów
    /// </summary>
    public enum VehicleType
    {
        Locomotive,     // Lokomotywa
        FreightCar,     // Wagon towarowy
        PassengerCar,   // Wagon pasażerski
        TankCar         // Wagon cysterna
    }
}
