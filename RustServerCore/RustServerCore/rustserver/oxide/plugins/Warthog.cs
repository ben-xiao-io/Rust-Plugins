using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using ProtoBuf;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Warthog", "Bing Chilling", "0.1.0")]
    [Description("Spawn warthog machine.")]

    public class Warthog : CovalencePlugin
    {
        public string SpawnPermission = "warthog.spawn";
        public string CoolDownPermission = "warthog.cooldown";
        private static LayerMask MASKS = LayerMask.GetMask("Construction", "Default", "Deployed", "Resource", "Terrain", "Water", "World");
        private const string TransportHeliPrefab = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        private const string SwitchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private const string AutoTurretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";

        private static Dictionary<ulong, float> coolDownMap = new Dictionary<ulong, float>();
        private static HashSet<AutoTurret> warthogTurrets = new HashSet<AutoTurret>();
        private static Dictionary<ulong, BaseVehicle> warthogMap = new Dictionary<ulong, BaseVehicle>();
        private static HashSet<BaseVehicle> warthogs = new HashSet<BaseVehicle>();

        private static Configuration cfg;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Spawn Cooldown")]
            public float SpawnCooldown = 30f;

            [JsonProperty(PropertyName = "Maximum Distance Allowed For Spawning Warthog")]
            public float MaxSpawnDistance = 50f;

            [JsonProperty(PropertyName = "Spawn Warthog With Fuel")]
            public bool SpawnWithFuel = true;

            [JsonProperty(PropertyName = "Warthog Automatic Fire Rate")]
            public float AutomaticFireRate = 0.001f;

            [JsonProperty(PropertyName = "Warthog Semi Automatic Fire Rate")]
            public float SemiAutomaticFireRate = 0.5f;

            [JsonProperty(PropertyName = "Warthog Aim Cone")]
            public float AimCone = 1f;
        }

        public class WarthogMetaData : MonoBehaviour
        {
            public ulong ownerID;
            public ulong entityID;
        }

        public class WarthogTurretMetaData : MonoBehaviour
        {
            public ulong ownerID;
            public ulong entityID;

            public float defaultRepeatDelay = 0.5f;
            public float defaultAiAimCone = 1f;
        }

        #region Config

        private void SaveConfig(Configuration cfg) => Config.WriteObject(cfg, true);

        private bool LoadConfigVariables()
        {
            base.LoadConfig();
            try
            {
                cfg = Config.ReadObject<Configuration>();
                if (cfg == null) throw new Exception();
            }
            catch
            {
                Console.Error.WriteLine("Configuration failed to load. Loading default configuration instead.");
                LoadDefaultConfig();
                return false;
            }

            SaveConfig(cfg);
            return true;
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating new config.");
            cfg = new Configuration();
            SaveConfig(cfg);
        }

        #endregion

        #region Init

        private void Init()
        {
            LoadConfigVariables();
            permission.RegisterPermission(SpawnPermission, this);
            permission.RegisterPermission(CoolDownPermission, this);
            Puts("Warthog has been initialized.");
        }

        private void Unload()
        {
            ClearAllWarthogs();
        }

        #endregion

        #region Commands

        [Command("warthog")]
        private void WarthogCommand(IPlayer player, string cmd, string[] args)
        {
            if (!player.HasPermission(SpawnPermission))
            {
                player.Reply("No permission to spawn warthog");
                return;
            };

            BasePlayer basePlayer = (BasePlayer)player.Object;
            ulong playerID = basePlayer.userID;
            float lastUsed;

            if (!coolDownMap.TryGetValue(playerID, out lastUsed))
            {
                coolDownMap.Add(playerID, Time.time);
                lastUsed = 0f;
            };

            float currTime = Time.time;
            float canUseTime = (lastUsed + cfg.SpawnCooldown);

            if (!player.HasPermission(CoolDownPermission))
            {
                if (currTime < canUseTime)
                {
                    float remainingTime = canUseTime - currTime;
                    string displayTime = remainingTime.ToString("0.0 second(s)");
                    player.Reply($"Cannot spawn warthog for another {displayTime}");
                    return;
                }
            }

            if (!SpawnWarthog(player))
            {
                player.Reply("[ERROR] Couldn't spawn Warthog.");
                return;
            }

            coolDownMap[playerID] = Time.time;
            return;
        }

        #endregion

        #region Helpers

        private bool SpawnWarthog(IPlayer player)
        {
            BasePlayer basePlayer = (BasePlayer) player.Object;

            RaycastHit hitInfo;
            if (!Physics.Raycast(basePlayer.eyes.HeadRay(), out hitInfo, Mathf.Infinity, MASKS)) return false;

            var position = hitInfo.point + Vector3.up * 2f;
            var warthog = (BaseVehicle) GameManager.server.CreateEntity(TransportHeliPrefab, position);
            if (warthog == null) return false;
            warthog.OwnerID = basePlayer.userID;
            warthog.Spawn();

            if (cfg.SpawnWithFuel)
            {
                StorageContainer fuelContainer = warthog.GetFuelSystem()?.GetFuelContainer();
                if (fuelContainer != null)
                {
                    ItemManager.CreateByName("lowgradefuel", 500)?.MoveToContainer(fuelContainer.inventory);
                }
            }

            WarthogMetaData metadata = warthog.gameObject.AddComponent<WarthogMetaData>();
            metadata.entityID = warthog.net.ID.Value;
            metadata.ownerID = basePlayer.userID;

            if (!SpawnTurret(warthog, basePlayer))
            {
                warthog.KillMessage();
                player.Reply("Unable to spawn Warthog turrets");
                return false;
            }

            BaseVehicle prevWarthog;
            if (warthogMap.TryGetValue(basePlayer.userID, out prevWarthog))
            {
                if (!prevWarthog.IsDestroyed) prevWarthog.KillMessage();
                warthogMap.Remove(basePlayer.userID);
            }

            warthogMap.Add(basePlayer.userID, warthog);
            warthogs.Add(warthog);

            return true;
        }

        private bool SpawnTurret(BaseVehicle warthog, BasePlayer basePlayer)
        {
            AutoTurret autoTurretRight = (AutoTurret) GameManager.server.CreateEntity(AutoTurretPrefab, warthog.transform.position);
            if (autoTurretRight == null) return false;

            autoTurretRight.SetFlag(BaseEntity.Flags.Reserved8, true);
            autoTurretRight.SetParent(warthog);
            autoTurretRight.pickup.enabled = false;
            autoTurretRight._limitedNetworking = false;
            autoTurretRight.transform.localPosition = new Vector3(1.8f, 1f, -1.0f);
            autoTurretRight.transform.localRotation = Quaternion.Euler(new Vector3(0f, 90f, 0));
            RemoveColliderProtection(autoTurretRight);

            autoTurretRight.dropsLoot = false;
            autoTurretRight.sightRange = 50f;
            autoTurretRight.Spawn();

            if (!AddSwitch(autoTurretRight))
            {
                autoTurretRight.KillMessage();
                return false;
            }

            WarthogTurretMetaData metadata = autoTurretRight.gameObject.AddComponent<WarthogTurretMetaData>();
            metadata.entityID = autoTurretRight.net.ID.Value;
            metadata.ownerID = basePlayer.userID;

            warthogTurrets.Add(autoTurretRight);

            AuthorizePlayer(autoTurretRight, basePlayer);

            return true;
        }

        private bool AddSwitch(AutoTurret autoTurret)
        {
            ElectricSwitch electricalSwitch = (ElectricSwitch) GameManager.server.CreateEntity(SwitchPrefab, autoTurret.transform.localPosition);
            if (electricalSwitch == null) return false;
            electricalSwitch.transform.localPosition = new Vector3(0f, -0.60f, 0.3f);
            electricalSwitch.transform.localRotation = Quaternion.Euler(new Vector3(0f, 0f, 0f));
            electricalSwitch.pickup.enabled = false;
            electricalSwitch._limitedNetworking = false;
            electricalSwitch.SetParent(autoTurret);
            RemoveColliderProtection(electricalSwitch);
            electricalSwitch.Spawn();

            return true;
        }

        private static void RemoveColliderProtection(BaseEntity colliderEntity)
        {
            foreach (MeshCollider meshCollider in colliderEntity.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(meshCollider);
            }

            UnityEngine.Object.DestroyImmediate(colliderEntity.GetComponent<GroundWatch>());
        }

        private void AuthorizePlayer(AutoTurret autoTurret, BasePlayer player)
        {
            var playerId = new PlayerNameID { userid = player.userID, username = player.displayName };
            autoTurret.authorizedPlayers.Add(playerId);

            RelationshipManager.PlayerTeam team = player.Team;
            if (team == null) return;

            foreach(ulong memberId in team.members)
            {
                BasePlayer memberBase = BasePlayer.FindByID(memberId);
                if (memberBase.IsRealNull()) continue;

                var teamMemberNameId = new PlayerNameID { userid = memberBase.userID, username = memberBase.displayName };
                autoTurret.authorizedPlayers.Add(teamMemberNameId);
            }
        }

        private void ClearAllWarthogs()
        {
            Puts($"Clearing {warthogMap.Count} warthog(s)...");
            foreach (BaseVehicle warthog in warthogMap.Values)
            {
                warthog.KillMessage();
            }

            warthogMap.Clear();
            warthogs.Clear();
            warthogTurrets.Clear();
            coolDownMap.Clear();
        }

        #endregion

        #region Hooks

        private object OnTurretTarget(AutoTurret turret, BaseCombatEntity entity)
        {
            BaseVehicle warthog = (BaseVehicle) turret.GetParentEntity();
            if (warthog == null) return null;   

            BaseProjectile weapon = (BaseProjectile) turret.AttachedWeapon;
            if (weapon == null) return null;

            weapon.repeatDelay = (weapon.isSemiAuto) ? cfg.SemiAutomaticFireRate : cfg.AutomaticFireRate;
            weapon.aiAimCone = cfg.AimCone;
            return null;
        }

        private object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            BaseVehicle baseVehicle = entity.VehicleParent();
            if (baseVehicle == null) return null;

            WarthogMetaData metadata;
            if (!baseVehicle.TryGetComponent<WarthogMetaData>(out metadata)) return null;

            if (!warthogs.Contains(baseVehicle))
            {
                baseVehicle.KillMessage(); 
                player.ChatMessage("This Warthog is not owned by you or another player. Removing Warthog...");
                return null;
            }

            if (player.userID != metadata.ownerID)
            {
                player.ChatMessage("No permission to enter into this Warthog.");
                return false;
            }

            return null;
        }


        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            WarthogMetaData metadata;
            if (!entity.TryGetComponent<WarthogMetaData>(out metadata)) return;
            BasePlayer owner = BasePlayer.FindByID(metadata.ownerID);
            owner.ChatMessage("Your warthog has been destroyed!");
        }

        private object OnTurretShutdown(AutoTurret turret)
        {
            WarthogTurretMetaData warthogTurretMetaData;
            if (!turret.TryGetComponent<WarthogTurretMetaData>(out warthogTurretMetaData)) return null;

            BaseProjectile weapon = (BaseProjectile)turret.AttachedWeapon;
            if (weapon == null) return null;

            weapon.repeatDelay = warthogTurretMetaData.defaultRepeatDelay;
            weapon.aiAimCone = warthogTurretMetaData.defaultAiAimCone;

            return null;
        }


        private object OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            Puts(hitInfo.boneName);
            return null;
        }

        #endregion
    }
}
