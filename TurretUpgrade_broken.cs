using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Oxide.Plugins
{
    [Info("TurretUpgrade", "YourName", "1.0.0")]
    [Description("Vehicle turret upgrade - mounts an auto turret on the cockpit roof")]
    public class TurretUpgrade : RustPlugin
    {
        [PluginReference]
        private Plugin VehicleUpgradesCore;
        
        // Store turrets by vehicle ID
        private Dictionary<ulong, AutoTurret> vehicleTurrets = new Dictionary<ulong, AutoTurret>();
        private Dictionary<ulong, bool> turretPowerState = new Dictionary<ulong, bool>();
        private Dictionary<ulong, bool> lastEngineState = new Dictionary<ulong, bool>();
        private HashSet<NetworkableId> intentionallyDestroyedTurrets = new HashSet<NetworkableId>();
        
        // Configuration
        private float TurretHeightOffset = 0.325f; // Fine-tuned to sit properly on the roof
        private float TurretForwardOffset = -0.5f; // Positioned toward back of cockpit (negative = backward)
        private float TurretSideOffset = 0f; // Left/right adjustment from center
        
        #region IVehicleUpgrade Implementation
        
        public string UpgradeId => "turret";
        public string Name => "Auto Turret";
        public string Description => "Mounted auto turret - powered by vehicle engine";
        
        public Dictionary<string, int> RequiredItems => new Dictionary<string, int>
        {
            ["autoturret"] = 1,  // Requires one auto turret
            ["wiretool"] = 1     // Requires one wire tool
        };
        
        private void OnInstall(ModularCar vehicle, BasePlayer player)
        {
            Puts($"[TurretUpgrade] OnInstall called! Vehicle: {vehicle?.net?.ID.Value}, Player: {player?.displayName}");
            
            if (vehicle == null || vehicle.IsDestroyed)
            {
                Puts($"[TurretUpgrade] OnInstall: Vehicle is null or destroyed");
                return;
            }
            
            Puts($"[TurretUpgrade] Installing turret on vehicle {vehicle.net.ID.Value}");
            
            // Spawn and attach the turret
            var turret = SpawnTurretOnVehicle(vehicle);
            if (turret != null)
            {
                vehicleTurrets[vehicle.net.ID.Value] = turret;
                turretPowerState[vehicle.net.ID.Value] = false;
                lastEngineState[vehicle.net.ID.Value] = vehicle.IsOn();
                
                // Check if engine is already running
                if (vehicle.IsOn())
                {
                    PowerOnTurret(turret);
                    turretPowerState[vehicle.net.ID.Value] = true;
                }
                
                player.ChatMessage("Auto turret installed on vehicle roof!");
                player.ChatMessage("The turret will activate when the engine is running.");
                Effect.server.Run("assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab", vehicle.transform.position);
            }
            else
            {
                player.ChatMessage("Failed to install turret! Please try again.");
                PrintError($"[TurretUpgrade] Failed to spawn turret for vehicle {vehicle.net.ID.Value}");
            }
        }
        
        private void OnUninstall(ModularCar vehicle)
        {
            if (vehicle == null) return;
            
            Puts($"[TurretUpgrade] Removing turret from vehicle {vehicle.net.ID.Value}");
            
            // Remove the turret
            if (vehicleTurrets.ContainsKey(vehicle.net.ID.Value))
            {
                var turret = vehicleTurrets[vehicle.net.ID.Value];
                if (turret != null && !turret.IsDestroyed)
                {
                    // Save inventory items before destroying
                    DropTurretInventory(turret);
                    intentionallyDestroyedTurrets.Add(turret.net.ID);
                    turret.Kill();
                }
                
                vehicleTurrets.Remove(vehicle.net.ID.Value);
                turretPowerState.Remove(vehicle.net.ID.Value);
                lastEngineState.Remove(vehicle.net.ID.Value);
            }
        }
        
        public void OnVehicleEngineStart(ModularCar vehicle)
        {
            Puts($"[TurretUpgrade] OnVehicleEngineStart called by Core for vehicle {vehicle?.net?.ID.Value}");
            if (!vehicleTurrets.ContainsKey(vehicle.net.ID.Value)) 
            {
                Puts($"[TurretUpgrade] No turret found for vehicle {vehicle.net.ID.Value}");
                return;
            }
            
            var turret = vehicleTurrets[vehicle.net.ID.Value];
            if (turret != null && !turret.IsDestroyed)
            {
                PowerOnTurret(turret);
                turretPowerState[vehicle.net.ID.Value] = true;
                Puts($"[TurretUpgrade] Engine started - turret powered on for vehicle {vehicle.net.ID.Value}");
            }
        }
        
        public void OnVehicleEngineStop(ModularCar vehicle)
        {
            Puts($"[TurretUpgrade] OnVehicleEngineStop called for vehicle {vehicle?.net?.ID.Value}");
            if (!vehicleTurrets.ContainsKey(vehicle.net.ID.Value)) 
            {
                Puts($"[TurretUpgrade] No turret found in dictionary for vehicle {vehicle.net.ID.Value}");
                return;
            }
            
            var turret = vehicleTurrets[vehicle.net.ID.Value];
            if (turret != null && !turret.IsDestroyed)
            {
                PowerOffTurret(turret);
                turretPowerState[vehicle.net.ID.Value] = false;
                Puts($"[TurretUpgrade] Engine stopped - turret powered off for vehicle {vehicle.net.ID.Value}");
            }
        }
        
        public void OnVehicleDestroyed(ModularCar vehicle)
        {
            OnUninstall(vehicle);
        }
        
        public void OnCoreUnload()
        {
            // Clean up all turrets WITHOUT dropping inventory (it's being saved)
            foreach (var turret in vehicleTurrets.Values)
            {
                if (turret != null && !turret.IsDestroyed)
                {
                    // Don't drop inventory - it's being saved by the Core
                    intentionallyDestroyedTurrets.Add(turret.net.ID);
                    turret.Kill();
                }
            }
            vehicleTurrets.Clear();
            turretPowerState.Clear();
        }
        
        // Hook to detect when turret is destroyed by player action
        private void OnEntityKill(AutoTurret turret)
        {
            if (turret == null) return;
            HandleTurretDestroyed(turret);
        }
        
        // Also try OnEntityDeath hook
        private void OnEntityDeath(AutoTurret turret, HitInfo info)
        {
            if (turret == null) return;
            HandleTurretDestroyed(turret);
        }
        
        // Also try OnEntityDestroyed hook
        private void OnEntityDestroyed(AutoTurret turret)
        {
            if (turret == null) return;
            HandleTurretDestroyed(turret);
        }
        
        private void HandleTurretDestroyed(AutoTurret turret)
        {
            // Check if this was an intentional destruction (e.g., during reload/respawn)
            if (intentionallyDestroyedTurrets.Contains(turret.net.ID))
            {
                intentionallyDestroyedTurrets.Remove(turret.net.ID);
                Puts($"[TurretUpgrade] Turret {turret.net.ID} was intentionally destroyed, not removing upgrade");
                return;
            }
            
            // Check if this turret belongs to a vehicle
            ulong vehicleId = 0;
            foreach (var kvp in vehicleTurrets)
            {
                if (kvp.Value == turret)
                {
                    vehicleId = kvp.Key;
                    break;
                }
            }
            
            if (vehicleId > 0)
            {
                Puts($"[TurretUpgrade] Turret destroyed on vehicle {vehicleId}, removing upgrade");
                
                // Remove from our tracking
                vehicleTurrets.Remove(vehicleId);
                turretPowerState.Remove(vehicleId);
                lastEngineState.Remove(vehicleId);
                
                // Notify VehicleUpgradesCore to remove the upgrade
                if (VehicleUpgradesCore != null)
                {
                    VehicleUpgradesCore.Call("RemoveUpgrade", vehicleId, "turret");
                    Puts($"[TurretUpgrade] Notified VehicleUpgradesCore to remove turret upgrade from vehicle {vehicleId}");
                }
            }
        }
        
        #endregion
        
        #region Oxide Hooks
        
        private int registrationAttempts = 0;
        private const int MAX_REGISTRATION_ATTEMPTS = 30;
        
        private Timer engineCheckTimer;
        
        private void Init()
        {
            Puts($"TurretUpgrade v1.0.0 initializing...");
            registrationAttempts = 0;
            AttemptRegistration();
            
            // Start engine state monitoring timer with a longer interval to prevent freezing
            // Changed from 1 second to 2 seconds and store reference for cleanup
            engineCheckTimer = timer.Every(2f, () => CheckEngineStates());
        }
        
        private bool isCheckingEngines = false;
        
        private void CheckEngineStates()
        {
            // Prevent concurrent execution
            if (isCheckingEngines) return;
            isCheckingEngines = true;
            
            try
            {
                // Don't create a copy of the dictionary - iterate directly
                // This prevents memory allocation issues
                var vehicleIds = vehicleTurrets.Keys.ToArray();
                
                foreach (var vehicleId in vehicleIds)
                {
                    if (!vehicleTurrets.ContainsKey(vehicleId)) continue;
                    
                    var turret = vehicleTurrets[vehicleId];
                
                if (turret == null || turret.IsDestroyed) 
                {
                    vehicleTurrets.Remove(vehicleId);
                    continue;
                }
                
                // Find the vehicle
                var vehicle = BaseNetworkable.serverEntities.Find(new NetworkableId(vehicleId)) as ModularCar;
                if (vehicle == null || vehicle.IsDestroyed) 
                {
                    vehicleTurrets.Remove(vehicleId);
                    continue;
                }
                
                bool currentEngineState = vehicle.IsOn();
                bool previousState = lastEngineState.ContainsKey(vehicleId) ? lastEngineState[vehicleId] : false;
                
                // Check if engine state changed
                if (currentEngineState != previousState)
                {
                    lastEngineState[vehicleId] = currentEngineState;
                    
                    if (currentEngineState)
                    {
                        // Engine started
                        if (!turretPowerState.ContainsKey(vehicleId) || !turretPowerState[vehicleId])
                        {
                            PowerOnTurret(turret);
                            turretPowerState[vehicleId] = true;
                            Puts($"[TurretUpgrade] Engine started (timer) - turret powered on for vehicle {vehicleId}");
                        }
                    }
                    else
                    {
                        // Engine stopped
                        if (!turretPowerState.ContainsKey(vehicleId) || turretPowerState[vehicleId])
                        {
                            PowerOffTurret(turret);
                            turretPowerState[vehicleId] = false;
                            Puts($"[TurretUpgrade] Engine stopped (timer) - turret powered off for vehicle {vehicleId}");
                        }
                    }
                }
                }
            }
            finally
            {
                isCheckingEngines = false;
            }
        }
        
        private void AttemptRegistration()
        {
            registrationAttempts++;
            
            if (VehicleUpgradesCore == null)
            {
                if (registrationAttempts <= MAX_REGISTRATION_ATTEMPTS)
                {
                    Puts($"[TurretUpgrade] VehicleUpgradesCore not found, attempt {registrationAttempts}/{MAX_REGISTRATION_ATTEMPTS}. Retrying in 1 second...");
                    timer.Once(1f, () => AttemptRegistration());
                }
                else
                {
                    PrintError($"[TurretUpgrade] Failed to find VehicleUpgradesCore after {MAX_REGISTRATION_ATTEMPTS} attempts. Turret upgrade will not function.");
                }
                return;
            }
            
            Puts($"[TurretUpgrade] Found VehicleUpgradesCore on attempt {registrationAttempts}, registering turret upgrade...");
            
            // Register with core
            var result = VehicleUpgradesCore.Call("API_RegisterUpgrade", this, UpgradeId, Name, Description, RequiredItems);
            
            if (result == null || (result is bool && !(bool)result))
            {
                if (registrationAttempts <= MAX_REGISTRATION_ATTEMPTS)
                {
                    Puts($"[TurretUpgrade] Registration failed, attempt {registrationAttempts}/{MAX_REGISTRATION_ATTEMPTS}. Retrying in 1 second...");
                    timer.Once(1f, () => AttemptRegistration());
                }
                else
                {
                    PrintError($"[TurretUpgrade] Failed to register with VehicleUpgradesCore after {MAX_REGISTRATION_ATTEMPTS} attempts.");
                }
            }
            else
            {
                Puts($"[TurretUpgrade] Successfully registered with VehicleUpgradesCore on attempt {registrationAttempts}");
            }
        }
        
        // Called by VehicleUpgradesCore when loading persistent data
        private void OnVehicleUpgradeLoaded(ModularCar vehicle, object upgradeData)
        {
            if (vehicle == null || vehicle.IsDestroyed) return;
            
            Puts($"[TurretUpgrade] Restoring turret on vehicle {vehicle.net.ID.Value} from storage");
            Puts($"[TurretUpgrade] UpgradeData type: {upgradeData?.GetType()?.Name ?? "null"}");
            
            // Remove any existing turret first (without dropping inventory - we'll restore it)
            if (vehicleTurrets.ContainsKey(vehicle.net.ID.Value))
            {
                var existingTurret = vehicleTurrets[vehicle.net.ID.Value];
                if (existingTurret != null && !existingTurret.IsDestroyed)
                {
                    Puts($"[TurretUpgrade] Removing existing turret before spawning new one (preserving inventory)");
                    // Mark this turret as intentionally destroyed so OnEntityKill doesn't remove the upgrade
                    intentionallyDestroyedTurrets.Add(existingTurret.net.ID);
                    // Unparent first to avoid physics/GC issues
                    existingTurret.SetParent(null, false, false);
                    // Don't drop inventory here - just kill the turret
                    existingTurret.Kill();
                }
                vehicleTurrets.Remove(vehicle.net.ID.Value);
            }
            
            // Spawn and attach the turret
            var turret = SpawnTurretOnVehicle(vehicle);
            if (turret != null)
            {
                vehicleTurrets[vehicle.net.ID.Value] = turret;
                turretPowerState[vehicle.net.ID.Value] = false;
                lastEngineState[vehicle.net.ID.Value] = vehicle.IsOn();
                
                // Restore turret inventory from saved data
                // Use NextTick instead of timer.Once to prevent blocking
                Puts($"[TurretUpgrade] Scheduling inventory restoration on next tick...");
                
                NextTick(() =>
                {
                    if (turret != null && !turret.IsDestroyed)
                    {
                        Puts($"[TurretUpgrade] Attempting to restore inventory after delay...");
                        // Ensure turret inventory is initialized
                        if (turret.inventory == null)
                        {
                            Puts($"[TurretUpgrade] ERROR: Turret inventory is null!");
                        }
                        else
                        {
                            Puts($"[TurretUpgrade] Turret inventory capacity: {turret.inventory.capacity}, current items: {turret.inventory.itemList.Count}");
                        }
                        RestoreTurretInventory(turret, upgradeData);
                    }
                    else
                    {
                        Puts($"[TurretUpgrade] ERROR: Turret was destroyed before restoration could complete!");
                    }
                });
                
                // Check if engine is running
                if (vehicle.IsOn())
                {
                    PowerOnTurret(turret);
                    turretPowerState[vehicle.net.ID.Value] = true;
                }
            }
        }
        
        private void Unload()
        {
            // Stop the engine check timer first to prevent freezing during unload
            engineCheckTimer?.Destroy();
            
            // Force save all turret data before unloading
            Puts("[TurretUpgrade] Unloading - forcing save of all turret data");
            
            // Tell the core to save our data
            if (VehicleUpgradesCore != null)
            {
                // Force a save by calling the Core's API_SaveData method
                VehicleUpgradesCore.Call("API_SaveData");
                Puts("[TurretUpgrade] Requested VehicleUpgradesCore to save data");
                
                // Unregister immediately (no timer to avoid crashes)
                VehicleUpgradesCore.Call("UnregisterUpgrade", UpgradeId);
            }
            
            // Clean up all turrets WITHOUT dropping inventory (it's being saved)
            foreach (var turret in vehicleTurrets.Values)
            {
                if (turret != null && !turret.IsDestroyed)
                {
                    // Don't drop inventory during unload - it will be restored on reload
                    intentionallyDestroyedTurrets.Add(turret.net.ID);
                    turret.Kill();
                }
            }
            vehicleTurrets.Clear();
            turretPowerState.Clear();
            lastEngineState.Clear();
        }
        
        // Hook when engine starts
        private void OnEngineStarted(BaseVehicle vehicle, BasePlayer driver)
        {
            Puts($"[TurretUpgrade] OnEngineStarted called for vehicle type: {vehicle?.GetType()?.Name}");
            
            var modularCar = vehicle as ModularCar;
            if (modularCar == null) 
            {
                Puts($"[TurretUpgrade] Vehicle is not a ModularCar, skipping");
                return;
            }
            
            // Check if vehicle has turret upgrade
            bool hasTurret = false;
            if (VehicleUpgradesCore != null)
            {
                var coreResult = VehicleUpgradesCore.Call("API_VehicleHasUpgrade", modularCar.net.ID.Value, UpgradeId);
                hasTurret = coreResult is bool && (bool)coreResult;
            }
            
            Puts($"[TurretUpgrade] Vehicle {modularCar.net.ID.Value} has turret upgrade: {hasTurret}, in dictionary: {vehicleTurrets.ContainsKey(modularCar.net.ID.Value)}");
            
            if (hasTurret && vehicleTurrets.ContainsKey(modularCar.net.ID.Value))
            {
                Puts($"[TurretUpgrade] Activating turret for vehicle {modularCar.net.ID.Value}");
                OnVehicleEngineStart(modularCar);
            }
        }
        
        // Hook when engine stops
        private void OnEngineStoppedFinished(BaseVehicle vehicle)
        {
            Puts($"[TurretUpgrade] OnEngineStoppedFinished called for vehicle type: {vehicle?.GetType()?.Name}");
            
            var modularCar = vehicle as ModularCar;
            if (modularCar == null) 
            {
                Puts($"[TurretUpgrade] Vehicle is not a ModularCar, skipping");
                return;
            }
            
            Puts($"[TurretUpgrade] Checking if vehicle {modularCar.net.ID.Value} has turret...");
            if (vehicleTurrets.ContainsKey(modularCar.net.ID.Value))
            {
                Puts($"[TurretUpgrade] Found turret for vehicle {modularCar.net.ID.Value}, stopping it");
                OnVehicleEngineStop(modularCar);
            }
            else
            {
                Puts($"[TurretUpgrade] No turret found for vehicle {modularCar.net.ID.Value}");
            }
        }
        
        #endregion
        
        #region Turret Management
        
        private AutoTurret SpawnTurretOnVehicle(ModularCar vehicle)
        {
            try
            {
                // Find the cockpit module
                BaseVehicleModule cockpitModule = null;
                foreach (var module in vehicle.AttachedModuleEntities)
                {
                    if (module.name.Contains("cockpit") || module.name.Contains("driver"))
                    {
                        cockpitModule = module;
                        Puts($"[TurretUpgrade] Found cockpit module: {module.name}");
                        break;
                    }
                }
                
                if (cockpitModule == null)
                {
                    PrintError($"[TurretUpgrade] No cockpit module found on vehicle {vehicle.net.ID.Value}");
                    return null;
                }
                
                // Clean up any existing turrets on this cockpit module (without dropping items)
                var existingTurrets = cockpitModule.GetComponentsInChildren<AutoTurret>();
                foreach (var oldTurret in existingTurrets)
                {
                    if (oldTurret != null && !oldTurret.IsDestroyed)
                    {
                        Puts($"[TurretUpgrade] Removing orphaned turret from cockpit (no inventory drop)");
                        // Unparent first to avoid physics issues
                        oldTurret.SetParent(null, false, false);
                        // Then kill without dropping inventory
                        intentionallyDestroyedTurrets.Add(oldTurret.net.ID);
                        oldTurret.Kill();
                    }
                }
                
                // Get the bounds of the cockpit module to find the exact roof position
                Bounds cockpitBounds = new Bounds(cockpitModule.transform.position, Vector3.zero);
                
                // Find all colliders in the cockpit module
                var colliders = cockpitModule.GetComponentsInChildren<Collider>();
                if (colliders.Length > 0)
                {
                    // Calculate the combined bounds of all colliders
                    foreach (var collider in colliders)
                    {
                        if (collider.enabled && !collider.isTrigger)
                        {
                            cockpitBounds.Encapsulate(collider.bounds);
                        }
                    }
                }
                else
                {
                    // Fallback to approximate bounds if no colliders found
                    cockpitBounds.size = new Vector3(1.5f, 1.8f, 2f);
                    Puts("[TurretUpgrade] Warning: No colliders found, using approximate cockpit size");
                }
                
                // Calculate the top center of the cockpit bounds
                float cockpitHeight = cockpitBounds.size.y;
                Vector3 roofPosition = cockpitModule.transform.position + (cockpitModule.transform.up * (cockpitHeight * 0.5f));
                
                // Adjust offsets based on cockpit type
                float forwardAdjust = TurretForwardOffset;
                float heightAdjust = TurretHeightOffset;
                
                // Different cockpit types have different shapes
                if (cockpitModule.name.Contains("armored"))
                {
                    // Armored cockpit is taller
                    heightAdjust += 0.1f;
                }
                else if (cockpitModule.name.Contains("with_engine"))
                {
                    // Cockpit with engine might need forward adjustment
                    forwardAdjust -= 0.2f;
                }
                
                // Apply configured offsets
                Vector3 turretPosition = roofPosition + 
                                        (cockpitModule.transform.up * heightAdjust) +
                                        (cockpitModule.transform.forward * forwardAdjust) +
                                        (cockpitModule.transform.right * TurretSideOffset);
                
                Puts($"[TurretUpgrade] Cockpit bounds: {cockpitBounds.size}, Height: {cockpitHeight}");
                Puts($"[TurretUpgrade] Module pos: {cockpitModule.transform.position}, Roof pos: {roofPosition}, Turret pos: {turretPosition}");
                
                // Create a visual marker at the spawn position (temporary - for debugging)
                Effect.server.Run("assets/prefabs/tools/map/genericradiusmarker.prefab", turretPosition, Vector3.zero);
                
                // Spawn the turret
                var turret = GameManager.server.CreateEntity("assets/prefabs/npc/autoturret/autoturret_deployed.prefab", 
                    turretPosition, cockpitModule.transform.rotation) as AutoTurret;
                
                if (turret == null)
                {
                    PrintError("[TurretUpgrade] Failed to create turret entity");
                    return null;
                }
                
                // Configure turret before spawning
                turret.authorizedPlayers.Clear();
                turret.SetFlag(BaseEntity.Flags.Reserved8, true); // Set as powered (when engine is on)
                turret.sightRange = 30f;
                
                // Spawn the turret
                turret.Spawn();
                
                // Parent the turret to the cockpit module
                turret.SetParent(cockpitModule, true, true);
                
                // Set turret health
                turret.InitializeHealth(turret.MaxHealth(), turret.MaxHealth());
                
                // Initially power off
                PowerOffTurret(turret);
                
                Puts($"[TurretUpgrade] Turret spawned successfully on vehicle {vehicle.net.ID.Value}");
                return turret;
            }
            catch (Exception ex)
            {
                PrintError($"[TurretUpgrade] Error spawning turret: {ex.Message}");
                return null;
            }
        }
        
        private void PowerOnTurret(AutoTurret turret)
        {
            if (turret == null || turret.IsDestroyed) return;
            
            turret.SetFlag(BaseEntity.Flags.Reserved8, true); // Powered flag
            turret.InitiateStartup();
            turret.SendNetworkUpdateImmediate();
            
            Puts($"[TurretUpgrade] Turret powered on");
        }
        
        private void PowerOffTurret(AutoTurret turret)
        {
            if (turret == null || turret.IsDestroyed) return;
            
            turret.SetFlag(BaseEntity.Flags.Reserved8, false); // Remove powered flag
            turret.InitiateShutdown();
            turret.SendNetworkUpdateImmediate();
            
            Puts($"[TurretUpgrade] Turret powered off");
        }
        
        private void DropTurretInventory(AutoTurret turret)
        {
            if (turret?.inventory == null) return;
            
            // Drop all items from turret inventory
            var items = turret.inventory.itemList.ToList();
            foreach (var item in items)
            {
                item.Drop(turret.transform.position + Vector3.up, Vector3.up * 2f);
            }
        }
        
        #endregion
        
        #region Inventory Persistence
        
        // Called by VehicleUpgradesCore to get custom data for saving
        private Dictionary<string, object> OnGetUpgradeData(ulong vehicleId)
        {
            Puts($"[TurretUpgrade] OnGetUpgradeData called for vehicle {vehicleId}");
            
            if (!vehicleTurrets.ContainsKey(vehicleId)) 
            {
                Puts($"[TurretUpgrade] No turret found for vehicle {vehicleId}");
                return null;
            }
            
            var turret = vehicleTurrets[vehicleId];
            if (turret == null || turret.IsDestroyed) 
            {
                Puts($"[TurretUpgrade] Turret is null or destroyed for vehicle {vehicleId}");
                return null;
            }
            
            var data = new Dictionary<string, object>();
            var items = new List<Dictionary<string, object>>();
            
            // Save turret inventory
            if (turret.inventory != null)
            {
                Puts($"[TurretUpgrade] Turret has {turret.inventory.itemList.Count} items in inventory");
                
                foreach (var item in turret.inventory.itemList)
                {
                    var itemData = new Dictionary<string, object>
                    {
                        ["itemid"] = item.info.itemid,
                        ["amount"] = item.amount,
                        ["skin"] = item.skin,
                        ["condition"] = item.condition,
                        ["position"] = item.position
                    };
                    
                    Puts($"[TurretUpgrade] Saving item: {item.info.shortname} x{item.amount} at position {item.position}");
                    
                    // Save weapon's loaded ammo
                    var weapon = item.GetHeldEntity() as BaseProjectile;
                    if (weapon != null && weapon.primaryMagazine != null)
                    {
                        itemData["loadedAmmo"] = weapon.primaryMagazine.contents;
                        itemData["ammoType"] = weapon.primaryMagazine.ammoType.itemid;
                        Puts($"[TurretUpgrade] - Weapon has {weapon.primaryMagazine.contents} rounds loaded");
                    }
                    
                    // Save weapon mods if any
                    if (item.contents != null)
                    {
                        var mods = new List<int>();
                        foreach (var mod in item.contents.itemList)
                        {
                            mods.Add(mod.info.itemid);
                        }
                        if (mods.Count > 0)
                        {
                            itemData["mods"] = mods;
                            Puts($"[TurretUpgrade] - Has {mods.Count} weapon mods");
                        }
                    }
                    
                    items.Add(itemData);
                }
            }
            
            data["items"] = items;
            
            // Save authorized players
            var authPlayers = new List<ulong>();
            foreach (var auth in turret.authorizedPlayers)
            {
                authPlayers.Add(auth.userid);
            }
            data["authorized"] = authPlayers;
            
            // Save turret settings
            data["sightRange"] = turret.sightRange;
            
            Puts($"[TurretUpgrade] Saved turret data for vehicle {vehicleId}: {items.Count} items, {authPlayers.Count} authorized players");
            
            // Debug: Log the data structure
            Puts($"[TurretUpgrade] Data keys: {string.Join(", ", data.Keys)}");
            
            return data;
        }
        
        private void RestoreTurretInventory(AutoTurret turret, object upgradeData)
        {
            if (turret == null || turret.IsDestroyed) 
            {
                Puts("[TurretUpgrade] Turret is null or destroyed, cannot restore inventory");
                return;
            }
            
            Puts($"[TurretUpgrade] RestoreTurretInventory called with data type: {upgradeData?.GetType()?.Name ?? "null"}");
            
            // The upgradeData is an UpgradeData object, we need to extract CustomData from it
            Dictionary<string, object> data = null;
            
            // Try to get CustomData from the UpgradeData object using reflection
            if (upgradeData != null)
            {
                var upgradeDataType = upgradeData.GetType();
                Puts($"[TurretUpgrade] Looking for CustomData field in type: {upgradeDataType.Name}");
                
                var customDataField = upgradeDataType.GetField("CustomData");
                if (customDataField != null)
                {
                    var customDataValue = customDataField.GetValue(upgradeData);
                    Puts($"[TurretUpgrade] CustomData field found, value type: {customDataValue?.GetType()?.Name ?? "null"}");
                    data = customDataValue as Dictionary<string, object>;
                    
                    if (data != null)
                    {
                        Puts($"[TurretUpgrade] CustomData dictionary has {data.Count} entries");
                        foreach (var key in data.Keys)
                        {
                            Puts($"[TurretUpgrade] CustomData key: {key}");
                        }
                    }
                }
                else
                {
                    Puts("[TurretUpgrade] CustomData field not found in UpgradeData");
                }
            }
            
            if (data == null || data.Count == 0) 
            {
                Puts("[TurretUpgrade] No saved custom data to restore");
                return;
            }
            
            // Restore items
            if (data.ContainsKey("items"))
            {
                Puts($"[TurretUpgrade] Found items key, value type: {data["items"]?.GetType()?.Name ?? "null"}");
                
                // Let's check what type the items really are
                var itemsObj = data["items"];
                if (itemsObj != null)
                {
                    var itemsType = itemsObj.GetType();
                    Puts($"[TurretUpgrade] Items actual type: {itemsType.FullName}");
                    
                    // Check if it's a generic list
                    if (itemsType.IsGenericType)
                    {
                        var genericArgs = itemsType.GetGenericArguments();
                        Puts($"[TurretUpgrade] Generic type arguments: {string.Join(", ", genericArgs.Select(t => t.Name))}");
                    }
                    
                    // Try to cast to IList to get count
                    var itemsAsList = itemsObj as System.Collections.IList;
                    if (itemsAsList != null)
                    {
                        Puts($"[TurretUpgrade] Items as IList has {itemsAsList.Count} elements");
                        
                        // Debug: Let's log each item in the list to see what we're getting
                        int idx = 0;
                        foreach (var item in itemsAsList)
                        {
                            Puts($"[TurretUpgrade] Item[{idx}] type: {item?.GetType()?.Name ?? "null"}");
                            idx++;
                        }
                    }
                }
                
                // Try different casting approaches
                var itemsList = data["items"] as List<Dictionary<string, object>>;
                var itemsObjList = data["items"] as List<object>;
                var itemsGenericList = data["items"] as System.Collections.IList;
                var itemsJArray = data["items"] as JArray;
                
                if (itemsJArray != null)
                {
                    // Handle JSON.NET JArray (common when loading from disk)
                    Puts($"[TurretUpgrade] Found items as JArray, restoring {itemsJArray.Count} items");
                    int restoredCount = 0;
                    
                    // Sort items: weapons FIRST, then ammo
                    var sortedTokens = itemsJArray.OrderBy(token => 
                    {
                        var itemJObj = token as JObject;
                        if (itemJObj == null) return 2; // Put invalid items last
                        var itemId = itemJObj["itemid"].ToObject<int>();
                        var itemDef = ItemManager.FindItemDefinition(itemId);
                        return itemDef?.category == ItemCategory.Weapon ? 0 : 1;
                    }).ToList();
                    
                    Puts($"[TurretUpgrade] Sorted JArray items (weapons first, then ammo)");
                    
                    foreach (var itemToken in sortedTokens)
                    {
                        var itemJObj = itemToken as JObject;
                        if (itemJObj == null) 
                        {
                            Puts($"[TurretUpgrade] Item is not a JObject, type: {itemToken?.GetType()?.Name}");
                            continue;
                        }
                        
                        var itemId = itemJObj["itemid"].ToObject<int>();
                        var amount = itemJObj["amount"].ToObject<int>();
                        var skin = itemJObj["skin"]?.ToObject<ulong>() ?? 0;
                        var condition = itemJObj["condition"]?.ToObject<float>() ?? 1f;
                        var position = itemJObj["position"]?.ToObject<int>() ?? -1;
                        
                        Puts($"[TurretUpgrade] Processing item: {ItemManager.FindItemDefinition(itemId)?.shortname ?? "unknown"} x{amount} at position {position}");
                        
                        var itemDef = ItemManager.FindItemDefinition(itemId);
                        Puts($"[TurretUpgrade] Processing JArray item: {itemDef?.shortname ?? "unknown"} x{amount} at position {position}, category: {itemDef?.category}");
                        
                        var item = ItemManager.CreateByItemID(itemId, amount, skin);
                        if (item != null)
                        {
                            item.condition = condition;
                            
                            // Restore weapon mods if any
                            if (itemJObj["mods"] != null)
                            {
                                var modsJArray = itemJObj["mods"] as JArray;
                                if (modsJArray != null)
                                {
                                    foreach (var modToken in modsJArray)
                                    {
                                        var modId = modToken.ToObject<int>();
                                        var modItem = ItemManager.CreateByItemID(modId, 1);
                                        if (modItem != null)
                                        {
                                            item.contents.Insert(modItem);
                                        }
                                    }
                                }
                            }
                            
                            // Auto turrets have special slot layout:
                            // Slot 0: Weapon only
                            // Slots 1-6: Ammo only
                            bool placed = false;
                            
                            if (item.info.category == ItemCategory.Weapon)
                            {
                                // Weapons go in slot 0
                                placed = item.MoveToContainer(turret.inventory, 0);
                                Puts($"[TurretUpgrade] Attempting to place weapon in slot 0: {(placed ? "success" : "failed")}");
                                if (placed)
                                {
                                    restoredCount++;
                                    Puts($"[TurretUpgrade] Restored item: {item.info.shortname} x{amount} at position {position}");
                                    
                                    // Restore weapon's loaded ammo
                                    if (itemJObj["loadedAmmo"] != null && itemJObj["ammoType"] != null)
                                    {
                                        var weapon = item.GetHeldEntity() as BaseProjectile;
                                        if (weapon != null && weapon.primaryMagazine != null)
                                        {
                                            var loadedAmmo = itemJObj["loadedAmmo"].ToObject<int>();
                                            var ammoType = itemJObj["ammoType"].ToObject<int>();
                                            
                                            weapon.primaryMagazine.contents = loadedAmmo;
                                            weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammoType);
                                            weapon.SendNetworkUpdateImmediate();
                                            
                                            Puts($"[TurretUpgrade] Restored {loadedAmmo} rounds in weapon magazine");
                                        }
                                    }
                                }
                                else
                                {
                                    Puts($"[TurretUpgrade] ERROR: Failed to restore weapon {item.info.shortname}");
                                    item.Remove();
                                }
                            }
                            else if (item.info.category == ItemCategory.Ammunition)
                            {
                                // Ammo handling - same as List<Dictionary> version
                                // Check if this ammo is compatible with the loaded weapon
                                var weaponItem = turret.inventory.GetSlot(0);
                                if (weaponItem != null)
                                {
                                    var weapon = weaponItem.GetHeldEntity() as BaseProjectile;
                                    if (weapon != null && weapon.primaryMagazine != null)
                                    {
                                        Puts($"[TurretUpgrade] Weapon {weaponItem.info.shortname} accepts ammo type: {weapon.primaryMagazine.ammoType?.shortname ?? "null"}");
                                    }
                                }
                                
                                Puts($"[TurretUpgrade] Trying to place ammo type: {item.info.shortname}");
                                
                                // First try the saved position if it's valid for ammo
                                if (position > 0 && position < turret.inventory.capacity)
                                {
                                    placed = item.MoveToContainer(turret.inventory, position);
                                    Puts($"[TurretUpgrade] Attempting to place ammo in slot {position}: {(placed ? "success" : "failed")}");
                                }
                                
                                // If that fails, try any ammo slot (1-6)
                                if (!placed)
                                {
                                    Puts($"[TurretUpgrade] First placement failed, trying other slots...");
                                    for (int slot = 1; slot < turret.inventory.capacity && !placed; slot++)
                                    {
                                        var existingItem = turret.inventory.GetSlot(slot);
                                        if (existingItem == null)
                                        {
                                            placed = item.MoveToContainer(turret.inventory, slot);
                                            if (placed)
                                            {
                                                Puts($"[TurretUpgrade] Placed ammo in slot {slot}");
                                            }
                                            else
                                            {
                                                Puts($"[TurretUpgrade] Failed to place in empty slot {slot}");
                                            }
                                        }
                                    }
                                    
                                    // Last resort - try without specifying slot
                                    if (!placed)
                                    {
                                        Puts($"[TurretUpgrade] Trying to place ammo without specifying slot...");
                                        placed = item.MoveToContainer(turret.inventory);
                                        if (placed)
                                        {
                                            Puts($"[TurretUpgrade] Placed ammo in inventory (auto-slot)");
                                        }
                                        else
                                        {
                                            // Try the GiveItem method as absolute last resort
                                            Puts($"[TurretUpgrade] Trying GiveItem method...");
                                            var given = turret.inventory.GiveItem(item);
                                            if (given)
                                            {
                                                placed = true;
                                                Puts($"[TurretUpgrade] Successfully gave ammo to turret using GiveItem!");
                                            }
                                            else
                                            {
                                                // Try setting parent and then moving
                                                Puts($"[TurretUpgrade] Trying to set parent to turret before moving...");
                                                item.parent = turret.inventory;
                                                placed = item.MoveToContainer(turret.inventory);
                                                if (placed)
                                                {
                                                    Puts($"[TurretUpgrade] SUCCESS! Placed ammo after setting parent!");
                                                }
                                                else
                                                {
                                                    // Absolute last resort - try direct insertion
                                                    Puts($"[TurretUpgrade] Last resort: trying direct inventory insertion...");
                                                    turret.inventory.itemList.Add(item);
                                                    item.parent = turret.inventory;
                                                    turret.inventory.MarkDirty();
                                                    turret.SendNetworkUpdate();
                                                    
                                                    // Check if it worked
                                                    if (turret.inventory.itemList.Contains(item))
                                                    {
                                                        placed = true;
                                                        Puts($"[TurretUpgrade] SUCCESS! Direct insertion worked!");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                if (placed)
                                {
                                    restoredCount++;
                                    Puts($"[TurretUpgrade] Restored ammo: {item.info.shortname} x{amount}");
                                }
                                else
                                {
                                    Puts($"[TurretUpgrade] ERROR: Failed to restore ammo {item.info.shortname} x{amount} - all slots full?");
                                    item.Remove();
                                }
                            }
                            else
                            {
                                // Other items (shouldn't happen in turret but handle gracefully)
                                placed = item.MoveToContainer(turret.inventory);
                                if (placed)
                                {
                                    restoredCount++;
                                    Puts($"[TurretUpgrade] Restored other item: {item.info.shortname} x{amount}");
                                }
                                else
                                {
                                    Puts($"[TurretUpgrade] ERROR: Failed to restore {item.info.shortname} x{amount}");
                                    item.Remove();
                                }
                            }
                        }
                    }
                    
                    Puts($"[TurretUpgrade] Successfully restored {restoredCount}/{itemsJArray.Count} items");
                }
                else if (itemsList != null)
                    {
                        Puts($"[TurretUpgrade] Found items as List<Dictionary>, restoring {itemsList.Count} items");
                        int restoredCount = 0;
                        
                        // Sort items: weapons FIRST, then ammo
                        // The turret REQUIRES the weapon to be mounted before it will accept compatible ammo!
                        var sortedItems = itemsList.OrderBy(itemData => 
                        {
                            var itemId = Convert.ToInt32(itemData["itemid"]);
                            var itemDef = ItemManager.FindItemDefinition(itemId);
                            return itemDef?.category == ItemCategory.Weapon ? 0 : 1; // Weapon gets priority 0, ammo gets 1
                        }).ToList();
                        
                        Puts($"[TurretUpgrade] Sorted items for restoration (weapons FIRST, then ammo)");
                        
                        foreach (var itemData in sortedItems)
                        {
                            if (itemData == null) continue;
                            
                            var itemId = Convert.ToInt32(itemData["itemid"]);
                            var amount = Convert.ToInt32(itemData["amount"]);
                            var skin = itemData.ContainsKey("skin") ? Convert.ToUInt64(itemData["skin"]) : 0;
                            var condition = itemData.ContainsKey("condition") ? Convert.ToSingle(itemData["condition"]) : 1f;
                            var position = itemData.ContainsKey("position") ? Convert.ToInt32(itemData["position"]) : -1;
                            
                            var itemDef = ItemManager.FindItemDefinition(itemId);
                            Puts($"[TurretUpgrade] Processing item: {itemDef?.shortname ?? "unknown"} x{amount} at position {position}, category: {itemDef?.category}");
                            
                            var item = ItemManager.CreateByItemID(itemId, amount, skin);
                            if (item != null)
                            {
                                item.condition = condition;
                                
                                // Auto turrets have special slot layout:
                                // Slot 0: Weapon only
                                // Slots 1-6: Ammo only
                                bool placed = false;
                                
                                if (item.info.category == ItemCategory.Weapon)
                                {
                                    // Weapons go in slot 0
                                    placed = item.MoveToContainer(turret.inventory, 0);
                                    Puts($"[TurretUpgrade] Attempting to place weapon in slot 0: {(placed ? "success" : "failed")}");
                                    if (placed)
                                    {
                                        restoredCount++;
                                        Puts($"[TurretUpgrade] Restored weapon: {item.info.shortname} x{amount}");
                                        
                                        // Restore weapon's loaded ammo
                                        if (itemData.ContainsKey("loadedAmmo") && itemData.ContainsKey("ammoType"))
                                        {
                                            var weapon = item.GetHeldEntity() as BaseProjectile;
                                            if (weapon != null && weapon.primaryMagazine != null)
                                            {
                                                var loadedAmmo = Convert.ToInt32(itemData["loadedAmmo"]);
                                                var ammoType = Convert.ToInt32(itemData["ammoType"]);
                                                
                                                weapon.primaryMagazine.contents = loadedAmmo;
                                                weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammoType);
                                                weapon.SendNetworkUpdateImmediate();
                                                
                                                Puts($"[TurretUpgrade] Restored {loadedAmmo} rounds in weapon magazine");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Puts($"[TurretUpgrade] ERROR: Failed to restore weapon {item.info.shortname}");
                                        item.Remove();
                                    }
                                }
                                else if (item.info.category == ItemCategory.Ammunition)
                                {
                                    // Check if this ammo is compatible with the loaded weapon
                                    var weaponItem = turret.inventory.GetSlot(0);
                                    if (weaponItem != null)
                                    {
                                        var weapon = weaponItem.GetHeldEntity() as BaseProjectile;
                                        if (weapon != null && weapon.primaryMagazine != null)
                                        {
                                            Puts($"[TurretUpgrade] Weapon {weaponItem.info.shortname} accepts ammo type: {weapon.primaryMagazine.ammoType?.shortname ?? "null"}");
                                        }
                                    }
                                    
                                    Puts($"[TurretUpgrade] Trying to place ammo type: {item.info.shortname}");
                                    
                                    // Ammo goes in slots 1-6
                                    // First try the saved position if it's valid for ammo
                                    if (position > 0 && position < turret.inventory.capacity)
                                    {
                                        // Check what's in this slot
                                        var existingItem = turret.inventory.GetSlot(position);
                                        if (existingItem != null)
                                        {
                                            Puts($"[TurretUpgrade] Slot {position} already contains: {existingItem.info.shortname} x{existingItem.amount}");
                                        }
                                        
                                        placed = item.MoveToContainer(turret.inventory, position);
                                        Puts($"[TurretUpgrade] Attempting to place ammo in slot {position}: {(placed ? "success" : "failed")}");
                                    }
                                    
                                    // If that fails, try any ammo slot (1-6)
                                    if (!placed)
                                    {
                                        Puts($"[TurretUpgrade] First placement failed, trying other slots...");
                                        for (int slot = 1; slot < turret.inventory.capacity && !placed; slot++)
                                        {
                                            var existingItem = turret.inventory.GetSlot(slot);
                                            if (existingItem == null)
                                            {
                                                placed = item.MoveToContainer(turret.inventory, slot);
                                                if (placed)
                                                {
                                                    Puts($"[TurretUpgrade] Placed ammo in slot {slot}");
                                                }
                                                else
                                                {
                                                    Puts($"[TurretUpgrade] Failed to place in empty slot {slot}");
                                                }
                                            }
                                        }
                                        
                                        // Last resort - try without specifying slot
                                        if (!placed)
                                        {
                                            Puts($"[TurretUpgrade] Trying to place ammo without specifying slot...");
                                            placed = item.MoveToContainer(turret.inventory);
                                            if (placed)
                                            {
                                                Puts($"[TurretUpgrade] Placed ammo in inventory (auto-slot)");
                                            }
                                            else
                                            {
                                                // Try the GiveItem method as absolute last resort
                                                Puts($"[TurretUpgrade] Trying GiveItem method...");
                                                var given = turret.inventory.GiveItem(item);
                                                if (given)
                                                {
                                                    placed = true;
                                                    Puts($"[TurretUpgrade] Successfully gave ammo to turret using GiveItem!");
                                                }
                                                else
                                                {
                                                    // Try setting parent and then moving
                                                    Puts($"[TurretUpgrade] Trying to set parent to turret before moving...");
                                                    item.parent = turret.inventory;
                                                    placed = item.MoveToContainer(turret.inventory);
                                                    if (placed)
                                                    {
                                                        Puts($"[TurretUpgrade] SUCCESS! Placed ammo after setting parent!");
                                                    }
                                                    else
                                                    {
                                                        // Absolute last resort - try direct insertion
                                                        Puts($"[TurretUpgrade] Last resort: trying direct inventory insertion...");
                                                        turret.inventory.itemList.Add(item);
                                                        item.parent = turret.inventory;
                                                        turret.inventory.MarkDirty();
                                                        turret.SendNetworkUpdate();
                                                        
                                                        // Check if it worked
                                                        if (turret.inventory.itemList.Contains(item))
                                                        {
                                                            placed = true;
                                                            Puts($"[TurretUpgrade] SUCCESS! Direct insertion worked!");
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    
                                    if (placed)
                                    {
                                        restoredCount++;
                                        Puts($"[TurretUpgrade] Restored ammo: {item.info.shortname} x{amount}");
                                    }
                                    else
                                    {
                                        Puts($"[TurretUpgrade] ERROR: Failed to restore ammo {item.info.shortname} x{amount} - all slots full?");
                                        item.Remove();
                                    }
                                }
                                else
                                {
                                    // Other items (shouldn't happen in turret but handle gracefully)
                                    placed = item.MoveToContainer(turret.inventory);
                                    if (placed)
                                    {
                                        restoredCount++;
                                        Puts($"[TurretUpgrade] Restored other item: {item.info.shortname} x{amount}");
                                    }
                                    else
                                    {
                                        Puts($"[TurretUpgrade] ERROR: Failed to restore {item.info.shortname} x{amount}");
                                        item.Remove();
                                    }
                                }
                            }
                        }
                        
                        Puts($"[TurretUpgrade] Successfully restored {restoredCount}/{itemsList.Count} items");
                    }
                    else if (itemsObjList != null)
                    {
                        Puts($"[TurretUpgrade] Found items as List<object>, restoring {itemsObjList.Count} items");
                        int restoredCount = 0;
                        
                        foreach (var itemObj in itemsObjList)
                        {
                            var itemData = itemObj as Dictionary<string, object>;
                            if (itemData == null) 
                            {
                                Puts($"[TurretUpgrade] Item is not a Dictionary, type: {itemObj?.GetType()?.Name}");
                                continue;
                            }
                            
                            var itemId = Convert.ToInt32(itemData["itemid"]);
                            var amount = Convert.ToInt32(itemData["amount"]);
                            var skin = itemData.ContainsKey("skin") ? Convert.ToUInt64(itemData["skin"]) : 0;
                            var condition = itemData.ContainsKey("condition") ? Convert.ToSingle(itemData["condition"]) : 1f;
                            var position = itemData.ContainsKey("position") ? Convert.ToInt32(itemData["position"]) : -1;
                            
                            Puts($"[TurretUpgrade] Processing item: {ItemManager.FindItemDefinition(itemId)?.shortname ?? "unknown"} x{amount}");
                            
                            var item = ItemManager.CreateByItemID(itemId, amount, skin);
                            if (item != null)
                            {
                                item.condition = condition;
                                item.position = position;
                                
                                // Restore weapon mods
                                if (itemData.ContainsKey("mods"))
                                {
                                    var mods = itemData["mods"] as List<object>;
                                    if (mods != null)
                                    {
                                        foreach (var modObj in mods)
                                        {
                                            var modId = Convert.ToInt32(modObj);
                                            var modItem = ItemManager.CreateByItemID(modId, 1);
                                            if (modItem != null)
                                            {
                                                item.contents.Insert(modItem);
                                            }
                                        }
                                    }
                                }
                                
                                // Try to place at exact position first
                                bool placed = false;
                                if (position >= 0 && position < turret.inventory.capacity)
                                {
                                    placed = item.MoveToContainer(turret.inventory, position);
                                    if (placed)
                                    {
                                        restoredCount++;
                                        Puts($"[TurretUpgrade] Restored item: {item.info.shortname} x{amount} at position {position}");
                                        
                                        // Restore weapon's loaded ammo
                                        if (itemData.ContainsKey("loadedAmmo") && itemData.ContainsKey("ammoType"))
                                        {
                                            var weapon = item.GetHeldEntity() as BaseProjectile;
                                            if (weapon != null && weapon.primaryMagazine != null)
                                            {
                                                var loadedAmmo = Convert.ToInt32(itemData["loadedAmmo"]);
                                                var ammoType = Convert.ToInt32(itemData["ammoType"]);
                                                
                                                weapon.primaryMagazine.contents = loadedAmmo;
                                                weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammoType);
                                                weapon.SendNetworkUpdateImmediate();
                                                
                                                Puts($"[TurretUpgrade] Restored {loadedAmmo} rounds in weapon magazine");
                                            }
                                        }
                                    }
                                }
                                
                                // If not placed at exact position, try any slot
                                if (!placed)
                                {
                                    placed = item.MoveToContainer(turret.inventory);
                                    if (placed)
                                    {
                                        restoredCount++;
                                        Puts($"[TurretUpgrade] Restored item: {item.info.shortname} x{amount} (any position)");
                                        
                                        // Restore weapon's loaded ammo
                                        if (itemData.ContainsKey("loadedAmmo") && itemData.ContainsKey("ammoType"))
                                        {
                                            var weapon = item.GetHeldEntity() as BaseProjectile;
                                            if (weapon != null && weapon.primaryMagazine != null)
                                            {
                                                var loadedAmmo = Convert.ToInt32(itemData["loadedAmmo"]);
                                                var ammoType = Convert.ToInt32(itemData["ammoType"]);
                                                
                                                weapon.primaryMagazine.contents = loadedAmmo;
                                                weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammoType);
                                                weapon.SendNetworkUpdateImmediate();
                                                
                                                Puts($"[TurretUpgrade] Restored {loadedAmmo} rounds in weapon magazine");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Puts($"[TurretUpgrade] ERROR: Failed to restore {item.info.shortname} x{amount} - turret inventory full?");
                                        item.Remove();
                                    }
                                }
                            }
                        }
                        
                        Puts($"[TurretUpgrade] Successfully restored {restoredCount}/{itemsObjList.Count} items");
                    }
                    else if (itemsGenericList != null)
                    {
                        Puts($"[TurretUpgrade] Found items as generic IList, restoring {itemsGenericList.Count} items");
                        int restoredCount = 0;
                        
                        foreach (var itemObj in itemsGenericList)
                        {
                            var itemData = itemObj as Dictionary<string, object>;
                            if (itemData == null) 
                            {
                                Puts($"[TurretUpgrade] Item is not a Dictionary, type: {itemObj?.GetType()?.Name}");
                                continue;
                            }
                            
                            var itemId = Convert.ToInt32(itemData["itemid"]);
                            var amount = Convert.ToInt32(itemData["amount"]);
                            var skin = itemData.ContainsKey("skin") ? Convert.ToUInt64(itemData["skin"]) : 0;
                            var condition = itemData.ContainsKey("condition") ? Convert.ToSingle(itemData["condition"]) : 1f;
                            var position = itemData.ContainsKey("position") ? Convert.ToInt32(itemData["position"]) : -1;
                            
                            Puts($"[TurretUpgrade] Processing item: {ItemManager.FindItemDefinition(itemId)?.shortname ?? "unknown"} x{amount}");
                            
                            var item = ItemManager.CreateByItemID(itemId, amount, skin);
                            if (item != null)
                            {
                                item.condition = condition;
                                item.position = position;
                                
                                // Try to place at exact position first
                                bool placed = false;
                                if (position >= 0 && position < turret.inventory.capacity)
                                {
                                    placed = item.MoveToContainer(turret.inventory, position);
                                }
                                
                                // If not placed at exact position, try any slot
                                if (!placed)
                                {
                                    placed = item.MoveToContainer(turret.inventory);
                                }
                                
                                if (placed)
                                {
                                    restoredCount++;
                                    Puts($"[TurretUpgrade] Restored item: {item.info.shortname} x{amount}");
                                    
                                    // Restore weapon's loaded ammo
                                    if (itemData.ContainsKey("loadedAmmo") && itemData.ContainsKey("ammoType"))
                                    {
                                        var weapon = item.GetHeldEntity() as BaseProjectile;
                                        if (weapon != null && weapon.primaryMagazine != null)
                                        {
                                            var loadedAmmo = Convert.ToInt32(itemData["loadedAmmo"]);
                                            var ammoType = Convert.ToInt32(itemData["ammoType"]);
                                            
                                            weapon.primaryMagazine.contents = loadedAmmo;
                                            weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammoType);
                                            weapon.SendNetworkUpdateImmediate();
                                            
                                            Puts($"[TurretUpgrade] Restored {loadedAmmo} rounds in weapon magazine");
                                        }
                                    }
                                }
                                else
                                {
                                    Puts($"[TurretUpgrade] ERROR: Failed to restore {item.info.shortname} x{amount}");
                                    item.Remove();
                                }
                            }
                        }
                        
                        Puts($"[TurretUpgrade] Successfully restored {restoredCount}/{itemsGenericList.Count} items");
                    }
                    else
                    {
                        Puts($"[TurretUpgrade] ERROR: Items data is not in any expected format");
                    }
                }
            
            // Restore authorized players - CRITICAL for not shooting friends!
            if (data.ContainsKey("authorized"))
            {
                Puts($"[TurretUpgrade] Found authorized key, value type: {data["authorized"]?.GetType()?.Name ?? "null"}");
                
                // Try multiple casting approaches
                var authPlayersJArray = data["authorized"] as JArray;
                var authPlayers = data["authorized"] as List<object>;
                var authPlayersList = data["authorized"] as List<ulong>;
                var authPlayersGeneric = data["authorized"] as System.Collections.IList;
                
                if (authPlayersJArray != null)
                {
                    Puts($"[TurretUpgrade] Restoring {authPlayersJArray.Count} authorized players from JArray");
                    
                    turret.authorizedPlayers.Clear();
                    int authCount = 0;
                    foreach (var playerToken in authPlayersJArray)
                    {
                        var playerId = playerToken.ToObject<ulong>();
                        var player = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
                        
                        turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                        {
                            userid = playerId,
                            username = player?.displayName ?? "Unknown"
                        });
                        authCount++;
                        Puts($"[TurretUpgrade] Authorized player {playerId} ({player?.displayName ?? "Unknown"})");
                    }
                    Puts($"[TurretUpgrade] Restored {authCount} authorized players");
                }
                else if (authPlayers != null)
                {
                    Puts($"[TurretUpgrade] Restoring {authPlayers.Count} authorized players");
                    
                    turret.authorizedPlayers.Clear();
                    int authCount = 0;
                    foreach (var playerObj in authPlayers)
                    {
                        var playerId = Convert.ToUInt64(playerObj);
                        var player = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
                        
                        turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                        {
                            userid = playerId,
                            username = player?.displayName ?? "Unknown"
                        });
                        authCount++;
                        Puts($"[TurretUpgrade] Authorized player {playerId} ({player?.displayName ?? "Unknown"})");
                    }
                    Puts($"[TurretUpgrade] Restored {authCount} authorized players");
                }
                else if (authPlayersList != null)
                {
                    Puts($"[TurretUpgrade] Restoring {authPlayersList.Count} authorized players (ulong list)");
                    
                    turret.authorizedPlayers.Clear();
                    int authCount = 0;
                    foreach (var playerId in authPlayersList)
                    {
                        var player = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
                        
                        turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                        {
                            userid = playerId,
                            username = player?.displayName ?? "Unknown"
                        });
                        authCount++;
                        Puts($"[TurretUpgrade] Authorized player {playerId} ({player?.displayName ?? "Unknown"})");
                    }
                    Puts($"[TurretUpgrade] Restored {authCount} authorized players");
                }
                else if (authPlayersGeneric != null)
                {
                    Puts($"[TurretUpgrade] Restoring {authPlayersGeneric.Count} authorized players (generic list)");
                    
                    turret.authorizedPlayers.Clear();
                    int authCount = 0;
                    foreach (var playerObj in authPlayersGeneric)
                    {
                        var playerId = Convert.ToUInt64(playerObj);
                        var player = BasePlayer.FindByID(playerId) ?? BasePlayer.FindSleeping(playerId);
                        
                        turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                        {
                            userid = playerId,
                            username = player?.displayName ?? "Unknown"
                        });
                        authCount++;
                        Puts($"[TurretUpgrade] Authorized player {playerId} ({player?.displayName ?? "Unknown"})");
                    }
                    Puts($"[TurretUpgrade] Restored {authCount} authorized players");
                }
                else
                {
                    Puts($"[TurretUpgrade] WARNING: Could not restore authorized players list!");
                }
            }
            else
            {
                Puts($"[TurretUpgrade] WARNING: No authorized players data found!");
            }
            
            // Restore turret settings
            if (data.ContainsKey("sightRange"))
            {
                turret.sightRange = Convert.ToSingle(data["sightRange"]);
            }
            
            turret.SendNetworkUpdateImmediate();
        }
        
        #endregion
        
        #region Authorization Management
        
        // Hook to authorize players who mount the vehicle
        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            if (mountable == null || player == null) return;
            
            var vehicleSeat = mountable as BaseVehicleSeat;
            if (vehicleSeat == null) return;
            
            var vehicle = vehicleSeat.VehicleParent() as ModularCar;
            if (vehicle == null) return;
            
            // Check if vehicle has turret
            if (!vehicleTurrets.ContainsKey(vehicle.net.ID.Value)) return;
            
            var turret = vehicleTurrets[vehicle.net.ID.Value];
            if (turret == null || turret.IsDestroyed) return;
            
            // Add player to authorized list if not already there
            if (!turret.IsAuthed(player))
            {
                turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                {
                    userid = player.userID,
                    username = player.displayName
                });
                turret.SendNetworkUpdateImmediate();
                
                Puts($"[TurretUpgrade] Authorized player {player.displayName} for vehicle turret");
            }
        }
        
        #endregion
        
        [ChatCommand("cleanturrets")]
        private void CleanTurretsCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("This command is admin only!");
                return;
            }
            
            int removed = 0;
            foreach (var kvp in vehicleTurrets.ToList())
            {
                var turret = kvp.Value;
                if (turret != null && !turret.IsDestroyed)
                {
                    intentionallyDestroyedTurrets.Add(turret.net.ID);
                    turret.Kill();
                    removed++;
                }
                vehicleTurrets.Remove(kvp.Key);
            }
            
            turretPowerState.Clear();
            lastEngineState.Clear();
            
            player.ChatMessage($"Cleaned up {removed} vehicle turrets");
            Puts($"[TurretUpgrade] Admin {player.displayName} cleaned up {removed} vehicle turrets");
        }
    }
}// Recompile Fri 29 Aug 2025 09:51:36 PM BST
// Force recompile Fri 29 Aug 2025 10:06:01 PM BST
