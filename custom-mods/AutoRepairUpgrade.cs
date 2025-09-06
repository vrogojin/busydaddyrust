using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AutoRepairUpgrade", "YourName", "1.4.0")]
    [Description("Auto-repair upgrade module for vehicles")]
    public class AutoRepairUpgrade : RustPlugin
    {
        [PluginReference]
        private Plugin VehicleUpgradesCore;
        
        private Timer autoRepairTimer;
        private HashSet<ulong> vehiclesWithAutoRepair = new HashSet<ulong>();
        
        // Configuration
        private float MinHealthToTriggerRepair = 40f; // Repair when below 40% health
        private float TargetHealthAfterRepair = 80f; // Target 80% health after repair
        private float RepairCheckInterval = 30f; // Check every 30 seconds
        private float EngineUseCooldownHours = 1f; // Don't repair if engine was used within 1 hour
        
        #region IVehicleUpgrade Implementation
        
        public string UpgradeId => "autorepair";
        public string Name => "Auto-Repairer";
        public string Description => "Automatically repairs vehicle using resources from storage";
        
        public Dictionary<string, int> RequiredItems => new Dictionary<string, int>
        {
            ["hammer"] = 1,
            ["techparts"] = 1,
            ["gears"] = 1
        };
        
        public void OnInstall(ModularCar vehicle, BasePlayer player)
        {
            Puts($"[AutoRepairUpgrade] OnInstall called for vehicle {vehicle.net.ID.Value}");
            
            // Check for storage/camper module
            bool hasStorage = false;
            foreach (var module in vehicle.AttachedModuleEntities)
            {
                if (module.name.Contains("storage", StringComparison.OrdinalIgnoreCase) || 
                    module.name.Contains("camper", StringComparison.OrdinalIgnoreCase))
                {
                    hasStorage = true;
                    break;
                }
            }
            
            if (!hasStorage)
            {
                player.ChatMessage("Vehicle must have a storage/camper module for auto-repairer!");
                
                // The core plugin should handle the refund and removal since installation failed
                // Just return without adding to our tracking
                return;
            }
            
            vehiclesWithAutoRepair.Add(vehicle.net.ID.Value);
            // Core handles persistence now
            Puts($"[AutoRepairUpgrade] Auto-repairer installed on vehicle {vehicle.net.ID.Value} - total vehicles with auto-repair: {vehiclesWithAutoRepair.Count}");
            
            // Start timer if not already running
            if (autoRepairTimer == null)
            {
                Puts($"[AutoRepairUpgrade] Starting auto-repair timer...");
                StartAutoRepairTimer();
            }
            else
            {
                Puts($"[AutoRepairUpgrade] Timer already running");
            }
            
            player.ChatMessage("Auto-repairer module installed successfully!");
            Effect.server.Run("assets/prefabs/deployable/repair bench/effects/skinchange_spraypaint.prefab", vehicle.transform.position);
        }
        
        public void OnUninstall(ModularCar vehicle)
        {
            vehiclesWithAutoRepair.Remove(vehicle.net.ID.Value);
            // Core handles persistence now
            Puts($"Auto-repairer removed from vehicle {vehicle.net.ID.Value}");
            
            // Stop timer if no vehicles have auto-repair
            if (vehiclesWithAutoRepair.Count == 0 && autoRepairTimer != null)
            {
                autoRepairTimer.Destroy();
                autoRepairTimer = null;
            }
        }
        
        public void OnVehicleEngineStart(ModularCar vehicle)
        {
            // Engine tracking is now handled by VehicleUpgradesCore
        }
        
        public void OnVehicleEngineStop(ModularCar vehicle)
        {
            // Engine tracking is now handled by VehicleUpgradesCore
        }
        
        public void OnVehicleDestroyed(ModularCar vehicle)
        {
            OnUninstall(vehicle);
        }
        
        public void OnCoreUnload()
        {
            // Clean up timer
            autoRepairTimer?.Destroy();
        }
        
        #endregion
        
        #region Oxide Hooks
        
        private int registrationAttempts = 0;
        private const int MAX_REGISTRATION_ATTEMPTS = 30;
        
        private void Init()
        {
            Puts($"AutoRepairUpgrade v1.4.0 initializing... (using centralized persistence)");
            registrationAttempts = 0;
            AttemptRegistration();
        }
        
        private void AttemptRegistration()
        {
            registrationAttempts++;
            
            if (VehicleUpgradesCore == null)
            {
                if (registrationAttempts <= MAX_REGISTRATION_ATTEMPTS)
                {
                    Puts($"[AutoRepairUpgrade] VehicleUpgradesCore not found, attempt {registrationAttempts}/{MAX_REGISTRATION_ATTEMPTS}. Retrying in 1 second...");
                    timer.Once(1f, () => AttemptRegistration());
                }
                else
                {
                    PrintError($"[AutoRepairUpgrade] Failed to find VehicleUpgradesCore after {MAX_REGISTRATION_ATTEMPTS} attempts. Auto-repair upgrade will not function.");
                }
                return;
            }
            
            Puts($"[AutoRepairUpgrade] Found VehicleUpgradesCore on attempt {registrationAttempts}, registering auto-repair upgrade...");
            
            // Register with core
            var result = VehicleUpgradesCore.Call("API_RegisterUpgrade", this, UpgradeId, Name, Description, RequiredItems);
            
            if (result == null || (result is bool && !(bool)result))
            {
                if (registrationAttempts <= MAX_REGISTRATION_ATTEMPTS)
                {
                    Puts($"[AutoRepairUpgrade] Registration failed, attempt {registrationAttempts}/{MAX_REGISTRATION_ATTEMPTS}. Retrying in 1 second...");
                    timer.Once(1f, () => AttemptRegistration());
                }
                else
                {
                    PrintError($"[AutoRepairUpgrade] Failed to register with VehicleUpgradesCore after {MAX_REGISTRATION_ATTEMPTS} attempts.");
                }
            }
            else
            {
                Puts($"[AutoRepairUpgrade] Successfully registered with VehicleUpgradesCore on attempt {registrationAttempts}");
                // Core will call OnVehicleUpgradeLoaded for each vehicle with auto-repair
            }
        }
        
        // Called by VehicleUpgradesCore when loading persistent data
        private void OnVehicleUpgradeLoaded(ModularCar vehicle, object upgradeData)
        {
            if (vehicle == null || vehicle.IsDestroyed) return;
            
            vehiclesWithAutoRepair.Add(vehicle.net.ID.Value);
            Puts($"[AutoRepairUpgrade] Restored auto-repair on vehicle {vehicle.net.ID.Value} from centralized storage");
            
            // Start timer if not already running
            if (autoRepairTimer == null && vehiclesWithAutoRepair.Count > 0)
            {
                Puts($"[AutoRepairUpgrade] Starting auto-repair timer for restored vehicles");
                StartAutoRepairTimer();
            }
        }
        
        private void Unload()
        {
            // NO LONGER SAVING OUR OWN DATA - Core handles persistence
            
            // Unregister from core
            if (VehicleUpgradesCore != null)
            {
                VehicleUpgradesCore.Call("UnregisterUpgrade", UpgradeId);
            }
            
            // Clean up timer
            autoRepairTimer?.Destroy();
        }
        
        // Hook to prevent decay on vehicles with auto-repair (only when conditions are met)
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            
            var vehicle = entity as ModularCar;
            if (vehicle == null)
            {
                // Check if it's a vehicle module
                var module = entity as BaseVehicleModule;
                if (module != null && module.Vehicle != null)
                {
                    vehicle = module.Vehicle as ModularCar;
                }
            }
            
            if (vehicle != null && vehicle.net != null && vehiclesWithAutoRepair.Contains(vehicle.net.ID.Value))
            {
                // Check if this is decay damage
                if (info.damageTypes != null && info.damageTypes.Has(Rust.DamageType.Decay))
                {
                    var vehicleId = vehicle.net.ID.Value;
                    
                    // Check if engine is currently running
                    if (vehicle.IsOn())
                    {
                        Puts($"[Auto-repair] Decay prevention disabled - engine is running on vehicle {vehicleId}");
                        return null; // Allow decay
                    }
                    
                    // Check if engine was used recently
                    var engineUsedRecently = VehicleUpgradesCore?.Call("API_WasEngineUsedRecently", vehicleId, EngineUseCooldownHours);
                    if (engineUsedRecently is bool && (bool)engineUsedRecently)
                    {
                        var timeSinceResult = VehicleUpgradesCore?.Call("API_GetTimeSinceEngineUse", vehicleId);
                        if (timeSinceResult is float)
                        {
                            float minutesAgo = (float)timeSinceResult;
                            Puts($"[Auto-repair] Decay prevention disabled - engine used {minutesAgo:F1} minutes ago (cooldown: {EngineUseCooldownHours} hours) on vehicle {vehicleId}");
                        }
                        return null; // Allow decay
                    }
                    
                    // Check overall health
                    float totalCurrentHealth = vehicle.health;
                    float totalMaxHealth = vehicle.MaxHealth();
                    
                    foreach (var mod in vehicle.AttachedModuleEntities)
                    {
                        totalCurrentHealth += mod.health;
                        totalMaxHealth += mod.MaxHealth();
                    }
                    
                    float healthPercentage = totalMaxHealth > 0 ? (totalCurrentHealth / totalMaxHealth) * 100f : 0f;
                    
                    if (healthPercentage >= MinHealthToTriggerRepair)
                    {
                        Puts($"[Auto-repair] Decay prevention disabled - vehicle {vehicleId} health at {healthPercentage:F1}% (above {MinHealthToTriggerRepair}% threshold)");
                        return null; // Allow decay
                    }
                    
                    // All conditions met - prevent decay
                    Puts($"[Auto-repair] Preventing decay damage on vehicle {vehicleId} - health at {healthPercentage:F1}%, engine off for >1h");
                    return true; // Cancel the damage
                }
            }
            
            return null;
        }
        
        #endregion
        
        #region Auto-Repair Logic
        
        private void StartAutoRepairTimer()
        {
            // Cancel existing timer if any
            autoRepairTimer?.Destroy();
            
            // Run auto-repair check every 30 seconds
            autoRepairTimer = timer.Every(RepairCheckInterval, () =>
            {
                if (vehiclesWithAutoRepair.Count > 0)
                {
                    CheckAllVehiclesForAutoRepair();
                }
            });
            
            Puts($"Auto-repair timer started - checking vehicles every {RepairCheckInterval} seconds");
        }
        
        private void CheckAllVehiclesForAutoRepair()
        {
            if (vehiclesWithAutoRepair.Count == 0) 
            {
                Puts("[Auto-repair] No vehicles registered for auto-repair");
                return;
            }
            
            Puts($"[Auto-repair] Checking {vehiclesWithAutoRepair.Count} vehicles for auto-repair...");
            
            // Don't process during server shutdown or save
            if (ServerMgr.Instance == null || ServerMgr.Instance.Restarting) return;
            
            int vehiclesChecked = 0;
            int vehiclesRepaired = 0;
            
            foreach (var vehicleId in vehiclesWithAutoRepair.ToList())
            {
                try
                {
                    // Find the vehicle
                    var vehicle = BaseNetworkable.serverEntities.Find(new NetworkableId(vehicleId)) as ModularCar;
                    if (vehicle == null || vehicle.IsDestroyed)
                    {
                        // Clean up destroyed vehicles
                        vehiclesWithAutoRepair.Remove(vehicleId);
                        continue;
                    }
                    
                    vehiclesChecked++;
                    
                    // Check if engine was used recently (includes check for currently running)
                    var engineUsedRecently = VehicleUpgradesCore?.Call("API_WasEngineUsedRecently", vehicleId, EngineUseCooldownHours);
                    if (engineUsedRecently is bool && (bool)engineUsedRecently)
                    {
                        var timeSinceResult = VehicleUpgradesCore?.Call("API_GetTimeSinceEngineUse", vehicleId);
                        if (timeSinceResult is float)
                        {
                            float minutesAgo = (float)timeSinceResult;
                            Puts($"[Auto-repair] Vehicle {vehicleId} - Engine was used {minutesAgo:F1} minutes ago (cooldown: {EngineUseCooldownHours} hours), skipping repair");
                        }
                        else
                        {
                            Puts($"[Auto-repair] Vehicle {vehicleId} - Engine is currently running, skipping repair");
                        }
                        continue;
                    }
                    
                    // Check if vehicle is on a powered lift
                    var onPoweredLift = VehicleUpgradesCore?.Call("API_IsVehicleOnPoweredLift", vehicleId);
                    if (onPoweredLift is bool && (bool)onPoweredLift)
                    {
                        Puts($"[Auto-repair] Vehicle {vehicleId} - On powered lift, skipping repair");
                        continue;
                    }
                    
                    // Calculate total health
                    float totalCurrentHealth = vehicle.health;
                    float totalMaxHealth = vehicle.MaxHealth();
                    
                    // Log chassis health
                    Puts($"[Auto-repair] Vehicle {vehicleId} - Chassis: {vehicle.health:F0}/{vehicle.MaxHealth():F0} ({(vehicle.health/vehicle.MaxHealth()*100):F1}%)");
                    
                    foreach (var module in vehicle.AttachedModuleEntities)
                    {
                        totalCurrentHealth += module.health;
                        totalMaxHealth += module.MaxHealth();
                        
                        // Log each module's health
                        Puts($"[Auto-repair] Vehicle {vehicleId} - Module {module.name}: {module.health:F0}/{module.MaxHealth():F0} ({(module.health/module.MaxHealth()*100):F1}%)");
                    }
                    
                    float healthPercentage = totalMaxHealth > 0 ? (totalCurrentHealth / totalMaxHealth) * 100f : 0f;
                    
                    Puts($"[Auto-repair] Vehicle {vehicleId} - Total: {totalCurrentHealth:F0}/{totalMaxHealth:F0} ({healthPercentage:F1}%) - Threshold: {MinHealthToTriggerRepair}%");
                    
                    // Check if below repair threshold
                    if (healthPercentage < MinHealthToTriggerRepair)
                    {
                        Puts($"[Auto-repair] Vehicle {vehicleId} at {healthPercentage:F1}% health - TRIGGERING REPAIR");
                        if (PerformVehicleRepair(vehicle))
                        {
                            vehiclesRepaired++;
                        }
                    }
                    else
                    {
                        Puts($"[Auto-repair] Vehicle {vehicleId} at {healthPercentage:F1}% health - above threshold, skipping repair");
                    }
                }
                catch (Exception ex)
                {
                    // Catch any errors to prevent timer from stopping
                    PrintError($"Error processing auto-repair for vehicle {vehicleId}: {ex.Message}");
                }
            }
            
            if (vehiclesChecked > 0)
            {
                Puts($"[Auto-repair] Cycle completed: {vehiclesChecked} vehicles checked, {vehiclesRepaired} repaired");
            }
        }
        
        private bool PerformVehicleRepair(ModularCar vehicle)
        {
            Puts($"[Auto-repair] PerformVehicleRepair called for vehicle {vehicle.net.ID.Value}");
            
            // Find storage module
            BaseVehicleModule storageModule = null;
            foreach (var module in vehicle.AttachedModuleEntities)
            {
                Puts($"[Auto-repair] Checking module: {module.name}");
                if (module.name.Contains("storage", StringComparison.OrdinalIgnoreCase) || 
                    module.name.Contains("camper", StringComparison.OrdinalIgnoreCase))
                {
                    storageModule = module;
                    Puts($"[Auto-repair] Found storage module: {module.name}");
                    break;
                }
            }
            
            if (storageModule == null)
            {
                Puts($"[Auto-repair] ERROR: No storage module found on vehicle {vehicle.net.ID.Value}");
                return false;
            }
            
            // Get storage containers
            var storageContainers = storageModule.GetComponentsInChildren<StorageContainer>();
            if (storageContainers == null || storageContainers.Length == 0)
            {
                Puts($"[Auto-repair] No storage containers found in module");
                return false;
            }
            
            // Find the best storage container (prefer one with items)
            StorageContainer storageContainer = null;
            foreach (var storage in storageContainers)
            {
                if (storage != null && storage.inventory != null)
                {
                    if (storageContainer == null || storage.inventory.itemList.Count > 0)
                    {
                        storageContainer = storage;
                    }
                }
            }
            
            if (storageContainer == null || storageContainer.inventory == null)
            {
                Puts($"[Auto-repair] No valid storage container found");
                return false;
            }
            
            // Build repair queue
            var repairQueue = new List<RepairableComponent>();
            
            // Priority 1: Chassis
            repairQueue.Add(new RepairableComponent
            {
                Name = "Chassis",
                Entity = vehicle,
                CurrentHealth = vehicle.health,
                MaxHealth = vehicle.MaxHealth(),
                Priority = 1,
                IsChassis = true
            });
            
            // Priority 2-5: Modules
            foreach (var module in vehicle.AttachedModuleEntities)
            {
                if (module != null && !module.IsDestroyed)
                {
                    int priority = GetModulePriority(module.name);
                    repairQueue.Add(new RepairableComponent
                    {
                        Name = module.name,
                        Entity = module,
                        CurrentHealth = module.health,
                        MaxHealth = module.MaxHealth(),
                        Priority = priority,
                        IsChassis = false
                    });
                }
            }
            
            // Sort by priority
            repairQueue = repairQueue.OrderBy(x => x.Priority).ToList();
            
            // Get available resources
            var availableResources = GetAvailableResources(storageContainer.inventory);
            
            Puts($"[Auto-repair] Available resources in storage:");
            if (availableResources.Count == 0)
            {
                Puts($"[Auto-repair] ERROR: No resources found in storage!");
            }
            else
            {
                foreach (var resource in availableResources)
                {
                    Puts($"[Auto-repair]   - {resource.Key}: {resource.Value}");
                }
            }
            
            // Repair components
            float totalRepaired = 0f;
            var consumedResources = new Dictionary<string, int>();
            
            foreach (var component in repairQueue)
            {
                // Calculate target health
                float componentTargetHealth = component.MaxHealth * (TargetHealthAfterRepair / 100f);
                
                if (component.CurrentHealth >= componentTargetHealth)
                {
                    continue;
                }
                
                // Calculate repair needed
                float repairNeeded = componentTargetHealth - component.CurrentHealth;
                float fullRepairNeeded = component.MaxHealth - component.CurrentHealth;
                
                // Get repair cost
                var fullRepairCost = GetComponentRepairCost(component.Entity, fullRepairNeeded / component.MaxHealth);
                
                if (fullRepairCost.Count == 0)
                {
                    Puts($"[Auto-repair] No repair cost for {component.Name} - skipping");
                    continue;
                }
                
                Puts($"[Auto-repair] Repair cost for {component.Name} (to repair {fullRepairNeeded:F0} HP):");
                foreach (var cost in fullRepairCost)
                {
                    Puts($"[Auto-repair]   - {cost.Key}: {cost.Value}");
                }
                
                // Calculate what we can afford
                float affordablePercent = CalculateAffordableRepairPercent(fullRepairCost, availableResources, consumedResources);
                
                if (affordablePercent <= 0)
                {
                    continue;
                }
                
                // Calculate actual repair amount
                float actualRepairAmount = Mathf.Min(repairNeeded, fullRepairNeeded * affordablePercent);
                float newHealth = component.CurrentHealth + actualRepairAmount;
                
                // Calculate resources to consume
                var resourcesToConsume = new Dictionary<string, int>();
                foreach (var cost in fullRepairCost)
                {
                    int amount = Mathf.CeilToInt(cost.Value * affordablePercent);
                    resourcesToConsume[cost.Key] = amount;
                    
                    if (!consumedResources.ContainsKey(cost.Key))
                        consumedResources[cost.Key] = 0;
                    consumedResources[cost.Key] += amount;
                }
                
                // Apply repair
                if (component.IsChassis)
                {
                    vehicle.SetHealth(newHealth);
                    vehicle.SendNetworkUpdate();
                }
                else
                {
                    var module = component.Entity as BaseVehicleModule;
                    if (module != null)
                    {
                        module.SetHealth(newHealth);
                        module.SendNetworkUpdate();
                    }
                }
                
                totalRepaired += actualRepairAmount;
                
                Puts($"[Auto-repair] Repaired {component.Name} from {component.CurrentHealth:F0} to {newHealth:F0} HP");
            }
            
            // Consume resources
            if (consumedResources.Count > 0)
            {
                ConsumeResources(storageContainer.inventory, consumedResources);
            }
            
            // Play repair effect
            if (totalRepaired > 0)
            {
                Effect.server.Run("assets/bundled/prefabs/fx/build/repair.prefab", vehicle.transform.position);
                
                Puts($"[Auto-repair] === REPAIR SUMMARY for vehicle {vehicle.net.ID.Value} ===");
                Puts($"[Auto-repair] Total HP repaired: {totalRepaired:F0}");
                Puts($"[Auto-repair] Resources consumed:");
                foreach (var resource in consumedResources)
                {
                    Puts($"[Auto-repair]   - {resource.Key}: {resource.Value}");
                }
                
                // Log final health state
                float finalTotalHealth = vehicle.health;
                float finalTotalMaxHealth = vehicle.MaxHealth();
                foreach (var module in vehicle.AttachedModuleEntities)
                {
                    finalTotalHealth += module.health;
                    finalTotalMaxHealth += module.MaxHealth();
                }
                float finalHealthPercent = (finalTotalHealth / finalTotalMaxHealth) * 100f;
                Puts($"[Auto-repair] Final vehicle health: {finalTotalHealth:F0}/{finalTotalMaxHealth:F0} ({finalHealthPercent:F1}%)");
                
                return true;
            }
            else
            {
                Puts($"[Auto-repair] No repairs performed for vehicle {vehicle.net.ID.Value} - check resources and repair costs");
            }
            
            return false;
        }
        
        private class RepairableComponent
        {
            public string Name { get; set; }
            public BaseEntity Entity { get; set; }
            public float CurrentHealth { get; set; }
            public float MaxHealth { get; set; }
            public int Priority { get; set; }
            public bool IsChassis { get; set; }
        }
        
        private int GetModulePriority(string moduleName)
        {
            // Lower number = higher priority
            if (moduleName.Contains("cockpit", StringComparison.OrdinalIgnoreCase))
                return 2; // Driver seat - critical
            if (moduleName.Contains("engine", StringComparison.OrdinalIgnoreCase))
                return 3; // Engine - needed for movement
            if (moduleName.Contains("storage", StringComparison.OrdinalIgnoreCase))
                return 4; // Storage - important but not critical
            if (moduleName.Contains("camper", StringComparison.OrdinalIgnoreCase))
                return 4; // Camper - same as storage
            return 5; // Everything else
        }
        
        private Dictionary<string, int> GetAvailableResources(ItemContainer container)
        {
            var resources = new Dictionary<string, int>();
            
            foreach (var item in container.itemList)
            {
                if (item.info != null)
                {
                    string shortname = item.info.shortname;
                    if (!resources.ContainsKey(shortname))
                        resources[shortname] = 0;
                    resources[shortname] += item.amount;
                }
            }
            
            return resources;
        }
        
        private Dictionary<string, int> GetComponentRepairCost(BaseEntity entity, float repairPercent)
        {
            var cost = new Dictionary<string, int>();
            
            List<ItemAmount> repairCostList = null;
            
            if (entity is ModularCar car)
            {
                repairCostList = car.RepairCost(repairPercent);
            }
            else if (entity is BaseVehicleModule module)
            {
                repairCostList = module.RepairCost(repairPercent);
            }
            
            if (repairCostList != null)
            {
                foreach (var item in repairCostList)
                {
                    if (item.itemDef != null)
                    {
                        cost[item.itemDef.shortname] = Mathf.CeilToInt(item.amount);
                    }
                }
            }
            
            return cost;
        }
        
        private float CalculateAffordableRepairPercent(Dictionary<string, int> repairCost, Dictionary<string, int> availableResources, Dictionary<string, int> alreadyConsumed)
        {
            float minPercent = 1f;
            
            foreach (var cost in repairCost)
            {
                int available = availableResources.ContainsKey(cost.Key) ? availableResources[cost.Key] : 0;
                int consumed = alreadyConsumed.ContainsKey(cost.Key) ? alreadyConsumed[cost.Key] : 0;
                int remaining = available - consumed;
                
                if (remaining <= 0)
                    return 0f;
                
                float percent = (float)remaining / cost.Value;
                minPercent = Mathf.Min(minPercent, percent);
            }
            
            return Mathf.Clamp01(minPercent);
        }
        
        private void ConsumeResources(ItemContainer container, Dictionary<string, int> resources)
        {
            foreach (var resource in resources)
            {
                var itemDef = ItemManager.FindItemDefinition(resource.Key);
                if (itemDef != null)
                {
                    container.Take(null, itemDef.itemid, resource.Value);
                }
            }
        }
        
        // REMOVED LoadExistingVehicles - Core now handles persistence and calls OnVehicleUpgradeLoaded
        
        #endregion
        
        // Data persistence is now handled by VehicleUpgradesCore
        
        #region Commands
        
        [ChatCommand("forceautorepair")]
        private void ForceAutoRepairCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("Admin only command");
                return;
            }
            
            // Find nearest car
            var cars = UnityEngine.Object.FindObjectsOfType<ModularCar>();
            ModularCar nearestCar = null;
            float nearestDistance = float.MaxValue;
            
            foreach (var car in cars)
            {
                float distance = Vector3.Distance(player.transform.position, car.transform.position);
                if (distance < nearestDistance && distance < 10f)
                {
                    nearestDistance = distance;
                    nearestCar = car;
                }
            }
            
            if (nearestCar == null)
            {
                player.ChatMessage("No car found nearby!");
                return;
            }
            
            player.ChatMessage($"Force-adding vehicle {nearestCar.net.ID.Value} to auto-repair list");
            
            if (!vehiclesWithAutoRepair.Contains(nearestCar.net.ID.Value))
            {
                vehiclesWithAutoRepair.Add(nearestCar.net.ID.Value);
                player.ChatMessage($"Vehicle added. Total vehicles with auto-repair: {vehiclesWithAutoRepair.Count}");
                
                if (autoRepairTimer == null)
                {
                    StartAutoRepairTimer();
                    player.ChatMessage("Auto-repair timer started!");
                }
            }
            else
            {
                player.ChatMessage("Vehicle already in auto-repair list");
            }
        }
        
        [ChatCommand("checkautorepair")]
        private void CheckAutoRepairCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("Admin only command");
                return;
            }
            
            // Find nearest car
            var cars = UnityEngine.Object.FindObjectsOfType<ModularCar>();
            ModularCar nearestCar = null;
            float nearestDistance = float.MaxValue;
            
            foreach (var car in cars)
            {
                float distance = Vector3.Distance(player.transform.position, car.transform.position);
                if (distance < nearestDistance && distance < 10f)
                {
                    nearestDistance = distance;
                    nearestCar = car;
                }
            }
            
            if (nearestCar == null)
            {
                player.ChatMessage("No car found nearby!");
                return;
            }
            
            player.ChatMessage($"=== Vehicle {nearestCar.net.ID.Value} ===");
            
            // Check with core plugin
            var hasAutoRepair = VehicleUpgradesCore?.Call("VehicleHasUpgrade", nearestCar.net.ID.Value, UpgradeId);
            player.ChatMessage($"Core plugin says has auto-repair: {hasAutoRepair}");
            
            // Check our tracking
            bool inOurList = vehiclesWithAutoRepair.Contains(nearestCar.net.ID.Value);
            player.ChatMessage($"In our tracking list: {inOurList}");
            
            if (!inOurList && hasAutoRepair is bool && (bool)hasAutoRepair)
            {
                player.ChatMessage("Adding to tracking and starting timer...");
                vehiclesWithAutoRepair.Add(nearestCar.net.ID.Value);
                if (autoRepairTimer == null)
                {
                    StartAutoRepairTimer();
                }
            }
        }
        
        [ChatCommand("testautorepair")]
        private void TestAutoRepairCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("Admin only command");
                return;
            }
            
            player.ChatMessage($"=== Auto-Repair Status ===");
            player.ChatMessage($"Vehicles with auto-repair: {vehiclesWithAutoRepair.Count}");
            player.ChatMessage($"Timer active: {autoRepairTimer != null}");
            player.ChatMessage($"Check interval: {RepairCheckInterval} seconds");
            player.ChatMessage($"Repair threshold: {MinHealthToTriggerRepair}%");
            player.ChatMessage($"Target health: {TargetHealthAfterRepair}%");
            player.ChatMessage($"Engine use cooldown: {EngineUseCooldownHours} hours");
            player.ChatMessage($"Decay protection: ENABLED (v1.2.0)");
            
            if (vehiclesWithAutoRepair.Count == 0)
            {
                player.ChatMessage("No vehicles have auto-repair installed!");
                return;
            }
            
            // Show cooldown status for each vehicle
            foreach (var vehicleId in vehiclesWithAutoRepair)
            {
                var vehicle = BaseNetworkable.serverEntities.Find(new NetworkableId(vehicleId)) as ModularCar;
                if (vehicle != null)
                {
                    player.ChatMessage($"Vehicle {vehicleId}:");
                    
                    if (vehicle.IsOn())
                    {
                        player.ChatMessage("  - Engine is currently running");
                    }
                    
                    var timeSinceResult = VehicleUpgradesCore?.Call("API_GetTimeSinceEngineUse", vehicleId);
                    if (timeSinceResult is float)
                    {
                        float minutesAgo = (float)timeSinceResult;
                        player.ChatMessage($"  - Last engine use: {minutesAgo:F1} minutes ago");
                        if (minutesAgo < EngineUseCooldownHours * 60)
                        {
                            var remaining = (EngineUseCooldownHours * 60) - minutesAgo;
                            player.ChatMessage($"  - Cooldown remaining: {remaining:F1} minutes");
                        }
                        else
                        {
                            player.ChatMessage($"  - Cooldown expired - ready for auto-repair");
                        }
                    }
                    else
                    {
                        player.ChatMessage("  - No engine use recorded - ready for auto-repair");
                    }
                    
                    var onLift = VehicleUpgradesCore?.Call("API_IsVehicleOnPoweredLift", vehicleId);
                    if (onLift is bool && (bool)onLift)
                    {
                        player.ChatMessage("  - Currently on powered lift");
                    }
                }
            }
            
            player.ChatMessage("Manually triggering auto-repair check...");
            CheckAllVehiclesForAutoRepair();
        }
        
        #endregion
    }
}