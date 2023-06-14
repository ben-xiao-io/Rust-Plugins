using Oxide.Core.Libraries.Covalence;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using static OcclusionCulling;

namespace Oxide.Plugins
{
    [Info("TestPLugin", "Bing Chilling", "0.1.0")]
    [Description("Makes epic stuff happen")]

    public class TestPlugin : CovalencePlugin
    {
        private static LayerMask MASKS = LayerMask.GetMask("Construction", "Default", "Deployed", "Resource", "Terrain", "Water", "World");
        private const string BagPrefab = "assets/prefabs/deployable/sleeping bag/sleepingbag_leather_deployed.prefab";
        private const string SphereEnt = "assets/prefabs/visualization/sphere.prefab";
        private const string TransportHeliPrefab = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab";
        private const string SwitchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private const string AutoTurretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private static HashSet<BaseEntity> zones =  new HashSet<BaseEntity>();
        private static HashSet<AutoTurret> allTurrets = new HashSet<AutoTurret>();

        private void Init()
        {
            Puts("RustPlugin is born!");
        }

        [Command("test")]
        private void TestCommand(IPlayer player, string cmd, string[] args)
        {
            player.Reply("Test successful!");
            RaycastHit hitInfo;
            Vector3 playerPosition = new Vector3(player.Position().X, player.Position().Y, player.Position().Z);
            if (Physics.Raycast(playerPosition, Vector3.down.normalized, out hitInfo, MASKS))
            {
                Vector3 groundPostion = new Vector3(playerPosition.x, hitInfo.point.y, playerPosition.z);
                Vector3 groundVector = hitInfo.normal;
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, groundVector);

                Puts(
                    $"Ground at {playerPosition.x} , {playerPosition.y}, {playerPosition.z} " +
                    $"with Rotation: {rotation.x}, {rotation.y}, {rotation.z}, {rotation.w}"
                );

                SleepingBag sleepingBag = GameManager.server.CreateEntity(BagPrefab, groundPostion, rotation) as SleepingBag;
                if (sleepingBag == null)
                {
                    player.Reply($"Could not spawn bag!");
                    return;
                }

                sleepingBag.Spawn();
                return;

            }

            player.Reply("Unable to find ground!");
        }


        [Command("bubble")]
        private void BubbleCommand(IPlayer player, string cmd, string[] args)
        {
            float radius = 15;
            Vector3 playerPosition = new Vector3(player.Position().X, player.Position().Y, player.Position().Z);
            BaseEntity sphere = GameManager.server.CreateEntity(SphereEnt, playerPosition, new Quaternion(), true);
            SphereEntity ent = sphere.GetComponent<SphereEntity>();
            ent.currentRadius = radius * 2;
            ent.lerpSpeed = 0f;
            sphere.Spawn();
            //zones.Add(sphere);
        }

        [Command("clear")]
        private void ClearZoneCommand(IPlayer player, string cmd, string[] args)
        {
            foreach(BaseEntity zone in zones)
            {
                zone.KillMessage();
            }

            zones.Clear();

            UnityEngine.Object[] straysZones = GameObject.FindObjectsOfType(typeof(SphereEntity));
            foreach(UnityEngine.Object straysZone in straysZones)
            {
                BaseEntity zoneAsEntity = (BaseEntity) straysZone;
                zoneAsEntity.KillMessage();
            }

        }


        [Command("debug")]
        private void DebugCommand(IPlayer player, string cmd, string[] args)
        {
            BasePlayer playerObj = (BasePlayer) player.Object;
            RaycastHit hitInfo;

            if (!Physics.Raycast(playerObj.eyes.HeadRay(), out hitInfo))
            {
                player.Reply($"No entity found");
                return;
            }

            player.Reply("Send out ray!");

            BaseEntity entity = hitInfo.GetEntity();

            if (!entity)
            {
                player.Reply("Entity not valid");
                return;
            }

            player.Reply($"{entity.name}");

        }

        [Command("ac")]
        private void ACCommand(IPlayer player, string cmd, string[] args)
        {
            BasePlayer baseplayer = (BasePlayer) player.Object;

            // look pos
            RaycastHit hitInfo;
            if (!Physics.Raycast(baseplayer.eyes.HeadRay(), out hitInfo, Mathf.Infinity, MASKS))
            {
                player.Reply("Can't spawn AC130 here");
                return;
            }

            var position = hitInfo.point + Vector3.up * 2f;
            var transportHeli = (BaseVehicle) GameManager.server.CreateEntity(TransportHeliPrefab, baseplayer.transform.position);
            if (transportHeli == null)
            {
                player.Reply("Unable to spawn AC13");
                return;
            }
            transportHeli.OwnerID = baseplayer.userID;
            transportHeli.rigidBody.useGravity = false;
            transportHeli.Spawn();

            StorageContainer fuelContainer = transportHeli.GetFuelSystem()?.GetFuelContainer();
            ItemManager.CreateByName("lowgradefuel", 500)?.MoveToContainer(fuelContainer.inventory);

            // add turret 
            AutoTurret autoTurret1 = (AutoTurret) GameManager.server.CreateEntity(AutoTurretPrefab, transportHeli.transform.position);
            if (autoTurret1 == null)
            {
                player.Reply("Could not place turret1 on AC130");
                return;
            }
            autoTurret1.SetFlag(BaseEntity.Flags.Reserved8, true);
            autoTurret1.SetParent(transportHeli);
            autoTurret1.allowedContents = ItemContainer.ContentsType.Generic;
            autoTurret1.pickup.enabled = false;
            autoTurret1.transform.localPosition = new Vector3(0, 0.6f, 0f);
            autoTurret1.transform.localRotation = Quaternion.Euler(new Vector3(0, 0, 180));
            RemoveColliderProtection(autoTurret1);
            autoTurret1.sightRange = 100f;
            autoTurret1.eyePos.position += Vector3.left * 10f;
            autoTurret1.RCEyes.position += Vector3.left * 10f;
            autoTurret1.muzzlePos.position += Vector3.left * 10f;
            autoTurret1.socketTransform.position += Vector3.left * 10f;
            autoTurret1.gun_pitch.position += Vector3.left * 10f;
            autoTurret1.gun_yaw.position += Vector3.left * 10f;

            autoTurret1.eyePos.transform.position += Vector3.left * 10f;
            autoTurret1.RCEyes.transform.position += Vector3.left * 10f;
            autoTurret1.muzzlePos.transform.position += Vector3.left * 10f;
            autoTurret1.socketTransform.transform.position += Vector3.left * 10f;
            autoTurret1.gun_pitch.transform.position += Vector3.left * 10f;
            autoTurret1.gun_yaw.transform.position += Vector3.left * 10f;

            autoTurret1.rcTurnSensitivity = 0f;
            autoTurret1.Spawn();
            autoTurret1.aimCone = 0.1f;
            autoTurret1.rcIdentifier = "test";
            allTurrets.Add(autoTurret1);



            //AutoTurret autoTurret2 = (AutoTurret)GameManager.server.CreateEntity(AutoTurretPrefab, transportHeli.transform.position);
            //if (autoTurret1 == null)
            //{
            //    player.Reply("Could not place turret2 on AC130");
            //    return;
            //}
            //autoTurret2.SetFlag(BaseEntity.Flags.Reserved8, true);
            //autoTurret2.SetParent(transportHeli);
            //autoTurret2.allowedContents = ItemContainer.ContentsType.Generic;
            //autoTurret2.pickup.enabled = false;
            //autoTurret2.transform.localPosition = new Vector3(-1.8f, 1f, -1.0f);
            //RemoveColliderProtection(autoTurret2);
            //autoTurret2.Spawn();

            // auth turrets
            var playerId = new PlayerNameID{userid = baseplayer.userID, username = baseplayer.displayName};
            autoTurret1.authorizedPlayers.Add(playerId);
            //autoTurret2.authorizedPlayers.Add(playerId);

            // add switch
            ElectricSwitch backTurretSwitch1 = (ElectricSwitch)GameManager.server.CreateEntity(SwitchPrefab);
            if (backTurretSwitch1 == null)
            {
                player.Reply("Could not place switch on AC130");
                return;
            }
            backTurretSwitch1.SetParent(autoTurret1);
            backTurretSwitch1.transform.localPosition = new Vector3(0f, -0.6f, -0.3f);
            backTurretSwitch1.transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));
            backTurretSwitch1.pickup.enabled = false;
            backTurretSwitch1._limitedNetworking = false;
            RemoveColliderProtection(backTurretSwitch1);
            backTurretSwitch1.Spawn();
        }

        [Command("turret")]
        private void TurretCommand(IPlayer player, string cmd, string[] args)
        {
            BasePlayer baseplayer = (BasePlayer) player.Object;
            AutoTurret autoTurret = (AutoTurret) GameManager.server.CreateEntity(AutoTurretPrefab, baseplayer.transform.position);
            if (autoTurret == null)
            {
                player.Reply("Could not place turret1 on AC130");
                return;
            }

            // autoTurret.RCEyes.localPosition += Vector3.up * 2f; // works
            autoTurret.sightRange = 100f;

            //autoTurret.socketTransform.localPosition += Vector3.up * 5f;
            //autoTurret.socketTransform.position += Vector3.up * 5f;
            //autoTurret.socketTransform.transform.localPosition += Vector3.up * 5f;
            //autoTurret.socketTransform.transform.position += Vector3.up * 5f;
            autoTurret.dropsLoot = false;
            autoTurret.Spawn();

            //switch

            ElectricSwitch backTurretSwitch1 = (ElectricSwitch)GameManager.server.CreateEntity(SwitchPrefab);
            if (backTurretSwitch1 == null)
            {
                player.Reply("Could not place switch on AC130");
                return;
            }
            backTurretSwitch1.SetParent(autoTurret);
            backTurretSwitch1.transform.localPosition = new Vector3(0f, -0.6f, -0.3f);
            backTurretSwitch1.transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));
            backTurretSwitch1.pickup.enabled = false;
            backTurretSwitch1._limitedNetworking = false;
           
            RemoveColliderProtection(backTurretSwitch1);
            backTurretSwitch1.Spawn();

            return;
        }

        [Command("switch")]
        private void SwitchCommand(IPlayer player, string cmd, string[] args)
        {
            BasePlayer baseplayer = (BasePlayer)player.Object;
            // add switch
            ElectricSwitch backTurretSwitch = (ElectricSwitch)GameManager.server.CreateEntity(SwitchPrefab);
            if (backTurretSwitch == null)
            {
                player.Reply("Could not place switch on AC130");
                return;
            }
            var switchLocation = baseplayer.transform.position + new Vector3(0f, 1f, 0f);
            backTurretSwitch.transform.localPosition = switchLocation;
            backTurretSwitch.transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));
            backTurretSwitch.pickup.enabled = false;
            backTurretSwitch._limitedNetworking = false;
            RemoveColliderProtection(backTurretSwitch);
            backTurretSwitch.Spawn();
            player.Reply("Made switch!");
        }

        private static void RemoveColliderProtection(BaseEntity colliderEntity)
        {

            foreach (var meshCollider in colliderEntity.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(meshCollider);
            }

            UnityEngine.Object.DestroyImmediate(colliderEntity.GetComponent<GroundWatch>());
        }


        private void OnDoorOpened(Door door, BasePlayer player)
        {
            if (!door.IsLocked())
            {
                player.ChatMessage($"Hey! You should knock first");
            }

            return;
        }

        private object OnItemCraft(ItemCraftTask task, BasePlayer player, Item item)
        {
            task.endTime = 1f;
            player.ChatMessage("Item has been instantly crafted!");
            return null;
        }
    }
}
