using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using System;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using static DaggerfallWorkshop.Game.PlayerEnterExit;
using DaggerfallConnect.Utility;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallConnect.Arena2;
using System.Text.RegularExpressions;


/// <remarks>
///     RIP has three classes with several different tasks to perform. The brunt of the work being
/// done via static methods in <see cref="RIP.RipDeathHandler>RipDeathHandler</see>.
///
/// TASK 1)
///     Mod activation. RipDeathHandler performs activation of the mod itself as well as manging mod settings. The core aspect of
/// mod activation beyond what the API requires is to relpace the default <see cref="PlayerDeath.OnPlayerDeath">PlayerDeath.OnPlayerDeath</see>
/// global event handler with its own. This is what allows the mod to track deaths in the game and respond to them accordingly.
///
///     However, due to the nature of how DFU sets up the scene, when key objects are created, and the differences between loading a file
/// for the first time, subsequent times, and starting a new game this process can be convoluted and involves responding to several potential
/// game events, settings flags to disable duplicate responses, checking the values of key objects, and even the use of a coroutine with
/// a slight delay. Basically... if the mod stops working in future versions of DFU or on different platforms, or on different days of the week,
/// this will be the first place to look. Also note that the replacement of this event handler means that any future game features or mods that
/// expect the default death event to trigger as normal will definitely be in conflict with this mod.
///
/// 
/// TASK 2)
///     Death event handling. Upon death, settings will be checked for appropriate action including lives incrementing and potentially allowing
/// the gameover screen as normal. If the player is not destined for the grave just yet then two things happen: First) A standard lootable
/// container is created and the appropriate gear from the player is moved into it. A locally cached copy of this data is also stored so that
/// this lootable 'corpse' can be later recreated as the default one will not survive location switching or saving (take note, the lootable
/// container specfically has its <see cref="DaggerfallWorkshop.Game.Serialization.SerializableLootContainer">SerializableLootContainer</see>
/// component stripped to avoid issues with doubled loot upon reloading saved files. Second) The player is teleported to the appropriate
/// respawn location. If a tavern had been rented subsequent to installing the mod, that will be the location they respawn. Note that this
/// does not require the player to still have a rented room there. Only to have rented there as some point. If no tavern
/// had been cached, they will respawn at the default starting location outside of the starter dungeon just north of Gothway Garden.
///
///
/// TASK 3)
///     Another feature of the mod's setup is to respond to global events about the buildings the player is entering or exiting. Whenever it is
/// detected that they have entered a tavern a GameObject with the <see cref="RIP.RipTavernPoller">TavernPoller</see> component is created.
/// This object updates every frame and checks for updates to the player's rented rooms. If a change is noticed, that tavern is then cached
/// as the valid respawn point for the player. This poller object is removed when they exit the tavern.
///
/// 
/// TASK 4)
///    The mod also listens for changes to the player's current location. During each change, all cached corpses are checked for expirey times and
/// if any are found to be overdue they are removed from the cached list, thus stopping them from respawning. A message of this action is displayed
/// in-game to the player regardless of where they are in the world.
///
///     During this event it also checks to see if any cached corpses have a location matching the one entered. If they do, they are recreated
/// as a lootable container and their inventory is filled from the cached data (like before, this container has its
/// <see cref="DaggerfallWorkshop.Game.Serialization.SerializableLootContainer">SerializableLootContainer</see> component removed to avoid duping.
/// A message is displayed to the user letting them know their corpse or corpses are somewhere in this location.
///
///
/// TASK 5)
///     Any time a lootable container corpse object is created and a <see cref="RIP.RipCorpseLootPoller">RipCorpseLootPoller</see> component is attached.
/// This object updates every frame and checks to see if the contents of the corpses have changed. This is needed so that
/// we can properly write these changes back to the internally cached corpse data so it is saved properly in the event of a partially looted corpse.
/// As well, if a container is found to be empty, it is removed from the internal cache list and the container object is destroyed.
///
///     NOTE: I absolutely hate this poller method of checking containers for changes because it is messy, complicated, and prone to errors and exploits.
/// Hopefully a future version of DFU will supply events to these containers when their contents are changed so that we can properly respond to such updates.
/// Even worse, I can't currently tell when the player has finished with the inventory so it simply deletes the container when it is empty.... but if the player
/// take everything out and without closing the window attempts to put something back in... bad things happen.
///
///     WARNING: There are at least two exploits here.
/// 1) Because the contents poller only checks for the change in inventory count and nothing else, that means the player could potentially swap a useless
/// item for a good one, close the container, save, reload, and then have a duplicate of that good item to loot.
///
/// 2) Stacks are not checked in detail at all so the player could do weird things with shifting stacks around so that the total item count is the same.
/// If they then close the container and reopen it, the original contents would still be present.
///
/// Please, nobody tell the Spiffing Brit!
/// 
/// </remarks>
namespace RIP
{
    /// <summary>
    /// TODO:
    ///     -Add a tool for saving potential respawn locations.
    ///         -Load saved locations at runtime and use them as random respawn points when no inn is available
    ///     -BUG: corpse sync on rapid reload is back!!
    ///     -randomly pick a city and a tavern if no rented rooms are available
    ///         -putting the player outside will ensure certain death due to elements
    ///     -support for player house?
    ///     -UI for fixing broken races on first spawn after character creation
    ///         -zero-stats will kill us instantly, detect this and allow a chance to fix it rather than have a loop of death!
    /// 
    /// TESTS LEFT:
    ///     -test last-rented room mode works
    ///     -test that valid rented room mode works
    ///     -test wilderness respawn works
    ///     -test loading between scene with corpses and scene with no corpses (outside)
    ///     -adjusting max corpse count mid-game
    ///     
    ///
    /// FUTURE FEATURES
    ///     -Stop Racial mods from starting a death-loop at the very start of the game.
    ///     -More respawn location options, like the player's house or temples
    ///     -Support for reducing XP
    ///     -Support for dropping cart contents
    ///     -Support for localization
    ///     -Support for external mod intertaction
    ///         -Allow registering callbacks for pre-respawn and post respawn events
    ///     
    /// KNOWN ISSUES:
    ///
    ///     -In order to ensure a death-loop didn't occur I had to take a heavy hand on curing negative effects on respawn.
    ///      I may go back at some point and try to filter things out based on severity and incubation periods but for now
    ///      I felt that in order to ensure the game was playable after a normal game-over state, this was the safest bet.
    ///      Note that upon respawn RIP will cure you of *ALL* ill-effects, even ones that were meant to permanently affect stats and skills.
    /// 
    ///     -When loading a saved game after having already started a game this play session, if any corpses exist in the
    ///      location being loaded there is a small chance due to a race condition that the corpses might correspond to the
    ///      state of the game previous to the reload.
    ///      NOTE: Currently a delayed coroutine is the only thing stopping this from happening all of the time.
    ///
    ///     -I have done my best to account for location-offset changes and even terrain sampler changes and when all else fails I even attempt
    ///      to use raycasting to reposition players and corpses but my limited resources only go so far. If you find examples
    ///      where players or coprses are not on the ground after death, please try to find a reproduceable save file and let me know.
    ///
    ///     -There are some duping exploits that can be used due to the lazy way in which I'm testing changes to corpse inventory.
    ///
    ///     -Other mods that rely on death to reset state will not be compatible with this mod. Known mods with issues are 'Death Date'.
    ///      Some racial mods have the ability to reduce a player's stats to zero at the very start, thus killing them. In order to avoid
    ///      a death loop RIP will ensure all player stats are a minimum of 1 when the game first starts.
    ///      
    ///     -A large number of existing mods out there absolutely PUKE errors all of the time. If one of them does this at a vital point it can completely
    ///      stop DFU from registering certain events or mods. I have throughouly tested this mod in isolation and with other mods and the only time it seems
    ///      to fail is when I have a lot of additional mods of questionable stability are installed. If you are experiencing issues, please try disabling other
    ///      mods and then saving your file with those mods disabled. This might not fix all issues however. If the issue still persists try to
    ///      reproduce it in a fresh game without any other mods.
    ///      If after this point you STILL have issues then it is clearly RIP that is at fault... probably... unless it's just DFU being DFU.
    ///     
    /// </summary>
    public class RipDeathHandler
    {
        #region Faux-Enums
        //since dynamically compiled mods can't use enums, these constants take their place
        //UPDATE: Technically not needed now since I decided to go with the precompiled option
        //but hey... compatibility in case I change my mind?
        //public const int RespawnTypeIdNone = 0;
        //public const int RespawnTypeIdRentedRoom = 1;
        //public const int RespawnTypeId
        public enum RespawnType
        {
            Unknown,
            RentedRoom,
            RandomRoom,
        }

        public const int UnconciousModeFixed = 0;
        public const int UnconciousModeDistance = 1;

        public static readonly float LocationLoadingTimeout = 60;
        public static readonly float HUDTextDisplayDelay = 1;
        public static readonly float CorpseRespawnDelay = 0.5f;

        public const string MSG_NextDeathPassthrough = "nextdeathpassthrough";
        public const string MSG_ForceDeath = "forcedeath";
        public const string MSG_Disable = "disablerip";
        public const string MSG_Enable = "enablerip";
        public const string MSG_CheckCorpse = "checkforcorpse";

        public enum RespawnModes
        {
            LastNonExpiredtavern,
            LastTavern,
            RandomTavern,
        }

        public enum RespawnResults
        {
            Failure = -1,
            Wilderness,
            FamiliarTavern,
            RandomTavern,
  
        }
        #endregion


        #region Containers
        /// <summary>
        /// The object that is serialized with the gamesave and stores persistent data for the mod.
        /// </summary>
        [FullSerializer.fsObject("v1")]
        public class RipSaveData : IHasModSaveData
        {
            public RipRespawnData RespawnData;
            public Dictionary<string, RipRespawnData> RespawnList;
            public RipCorpseData[] Corpses;
            public int LivesLeft;

            public Type SaveDataType { get { return typeof(RipSaveData); } }

            public object GetSaveData()
            {
                if (!UpdateCorpsesSize())
                    CullRottedCorpses();
                return new RipSaveData
                {
                    RespawnData = RipDeathHandler.RespawnData,
                    RespawnList = RipDeathHandler.RespawnList,
                    Corpses = RipDeathHandler.Corpses,
                    LivesLeft = RipDeathHandler.LivesLeft,
                };
            }

            public object NewSaveData()
            {
                var saveData = new RipSaveData()
                {
                    RespawnData = new RipRespawnData(),
                    RespawnList = new Dictionary<string, RipRespawnData>(),
                    Corpses = new RipCorpseData[MaxCorpses],
                    LivesLeft = MaxLives,
                };

                for (int i = 0; i < MaxCorpses; i++)
                {
                    Corpses[i] = new RipCorpseData();
                    Corpses[i].Loot = null;
                    Corpses[i].DropLocation = null;
                    Corpses[i].Spawned = false;
                }

                return saveData;
            }

            public void RestoreSaveData(object saveData)
            {
                Log("Restoring saved data");
                var data = (RipSaveData)saveData;
                RipDeathHandler.RespawnData = data.RespawnData;
                RipDeathHandler.RespawnList = data.RespawnList;
                RipDeathHandler.Corpses = data.Corpses;
                RipDeathHandler.LivesLeft = data.LivesLeft;

                if (!UpdateCorpsesSize())
                    CullRottedCorpses();
            }
        }

        [FullSerializer.fsObject("v1")]
        public struct RipRespawnData
        {
            public RespawnType RespawnTypeId;
            public int WorldPosX;
            public int WorldPosY;
            public PlayerPositionData_v1 AnchorPos;
        }

        [FullSerializer.fsObject("v1")]
        public class RipCorpseData : IComparer<RipCorpseData>
        {
            public int WorldPosX;
            public int WorldPosY;
            public int Region;
            public int Map;
            public int DropDate;
            public LootContainerData_v1 Loot;
            public PlayerPositionData_v1 DropLocation;
            [NonSerialized]
            public bool Spawned;

            public bool IsInside
            {
                get
                {
                    //note: some of the 'inside' flags for this structure are not useful since they don't update
                    //when leaving (like a tavern for example) but dungeon and building should be good and *should* cover all cases.... I think
                    return DropLocation.insideBuilding | DropLocation.insideDungeon;
                }
            }

            //compares corpse data by age
            public int Compare(RipCorpseData x, RipCorpseData y)
            {
                if (x.DropDate < y.DropDate) return -1;
                if (x.DropDate > y.DropDate) return 1;
                return 0;
            }

            /// <summary>
            /// Called by the RipCorpseLootPoller when the physical GameObject
            /// that represents this item has been destroyed and should no longer persist.
            /// </summary>
            public void Destroyed()
            {
                Loot = null;
                DropLocation = null;
                Spawned = false;
            }
        }
        #endregion


        #region Serialized Data
        static RipRespawnData RespawnData = new RipRespawnData();
        static Dictionary<string, RipRespawnData> RespawnList = new Dictionary<string, RipRespawnData>(5);
        static RipCorpseData[] Corpses = { new RipCorpseData() }; //1 corpse by default
        static int LivesLeft;
        #endregion


        #region Mod Settings
        static bool DropGoldOnDeath = true;
        static int GoldPercent = 100;
        static bool DropEquipmentOnDeath = true;
        static int EquipmentPercent = 100;
        static bool DropInventoryOnDeath = true;
        static int InventoryPercent = 100;
        static bool DropSpellBook = false;
        static bool DropQuestItems = false;
        static bool DropHorse = true;
        static bool DropCart = true;
        static bool DropCartContents = false;

        static bool LeaveCorpse = true;
        static bool CorpseCanRot = false;
        static int DaysToRot = 30;
        static int MaxCorpses = 1;
        static bool CanDie = false;
        static int DeathChance = 5;
        static bool CanLoseLives = false;
        static int MaxLives = 6;
        static bool ZeroStatsCauseDeath = false;
        static bool LoseXp = false;
        static int UnconciousMode = UnconciousModeDistance;
        static int UnconciousTime = 43200; //12 in-game hours
        static int MaxUnconciousTime = 7; //7 in-game days
        static bool EnhanceCorpseVisibility = true;

        static RespawnModes RespawnMode = RespawnModes.LastNonExpiredtavern;
        #endregion


        #region State Info
        static GameObject ParticlesPrefab = null;
        static GameObject TavernPollerGO = null;
        static Mod Mod;
        public static bool IsRIPActive { private set; get; }
        public static bool Loaded = false;
        public static DFLocation LastDeathLocation;
        static bool NextDeathPassthrough = false; //can be set from an external message. used to allow next death to occur as normal
        #endregion


        /// <summary>
        /// Returns the index of the first empty corpse or the oldest if none are empty.
        /// </summary>
        /// <returns></returns>
        public static int OldestCorpseIndex
        {
            get
            {
                int oldestDate = int.MaxValue;
                int oldestIndex = 0;
                for (int i = 0; i < Corpses.Length; i++)
                {
                    var corpse = Corpses[i];
                    if (corpse.Loot == null)
                        return i;

                    if(corpse.DropDate < oldestDate)
                    {
                        oldestDate = corpse.DropDate;
                        oldestIndex = i;
                    }
                }
                return oldestIndex;
            }
        }

        public delegate void PlayerRespawnEvent();
        public static event PlayerRespawnEvent OnPrePlayerRespawn;
        public static event PlayerRespawnEvent OnPostPlayerRespawn;


        #region Private Methods
        /// <summary>
        /// Initialization entry-point for DFU mod system.
        /// </summary>
        /// <param name="initParams"></param>
        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void InitMod(InitParams initParams)
        {
            Mod = initParams.Mod;
            Mod.SaveDataInterface = new RipSaveData();
            Mod.LoadSettingsCallback = HandleLoadSettings;
            Mod.LoadSettings();
            Log("RIP Mod Initialized");
            EnableRIP();
            ParticlesPrefab = Mod.GetAsset<GameObject>("CorpseParticles");
            Mod.MessageReceiver = ReceiveExternalMessage;
            Mod.IsReady = true;
        }

        /// <summary>
        /// Invoked by DFU's mod system. Begins overriding the default death event handlers
        /// as soon as a newgame session begins.
        /// </summary>
        /// <param name="initParams"></param>
        [Invoke(StateManager.StateTypes.Game)]
        public static void AwakeMod(InitParams initParams)
        {
            //we have to wait until the game has started to attach to these
            //overwrite default global death event handler
            PlayerDeath.OnPlayerDeath -= GameManager.Instance.StateManager.PlayerDeath_OnPlayerDeathHandler;
            PlayerDeath.OnPlayerDeath += HandleDeath;
            PlayerEnterExit.OnTransitionInterior += HandleTransition;   //we'll need this to track last known tavern to respawn to
            PlayerEnterExit.OnTransitionExterior += HandleTransition;
            PlayerEnterExit.OnTransitionDungeonInterior += HandleTransition;
            PlayerEnterExit.OnTransitionDungeonExterior += HandleTransition;
            PlayerGPS.OnEnterLocationRect += HandleEnterLocation;       //

            
            SpinWhileLevelLoads(LocationLoadingTimeout);
            HandleEnterLocation(GameManager.Instance.PlayerGPS.CurrentLocation);
            Log("RIP is now overriding the global death event handler.");

            //if (IsClimatesAndCaloriesModInstalled())
            //    Log("'Calories and Climates mod has been detected.");
        }

        /// <summary>
        /// Handles incoming messages from other mods.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="data"></param>
        /// <param name="callback"></param>
        static void ReceiveExternalMessage(string msg, object data, DFModMessageCallback callback)
        {
            switch(msg)
            {
                case MSG_Disable:
                    {
                        IsRIPActive = false;
                        break;
                    }
                case MSG_Enable:
                    {
                        IsRIPActive = true;
                        break;
                    }
                case MSG_ForceDeath:
                    {
                        ProcessDeathEvent(null, null);
                        break;
                    }
                case MSG_NextDeathPassthrough:
                    {
                        NextDeathPassthrough = true;
                        break;
                    }
                case MSG_CheckCorpse:
                    {
                        if (data == null)
                            ForceCheckForCorpses();
                        else if(data != null)
                        {
                            if (data is DFLocation loc)
                                ForceCheckForCorpses(loc);
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        static void EnableRIP()
        {
            StateManager.OnStartNewGame += (object sender, EventArgs args) =>
            {
                Log("Game Started");
                DelayedHUDText("RIP Is now active.");
                if (CanLoseLives) DelayedHUDText("You have " + LivesLeft + " lives remaining.");
            };
            SaveLoadManager.OnLoad += (SaveData_v1 saveData) =>
            {
                Loaded = true;
                Log("Savefile Loaded");
            };
            LivesLeft = MaxLives;
            IsRIPActive = true;
        }

        /// <summary>
        /// Applies mod settings changes both at load time and at runtime.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="change"></param>
        static void HandleLoadSettings(ModSettings settings, ModSettingsChange change)
        {
            DropGoldOnDeath = settings.GetValue<bool>("DropUponDeath", "Gold");
            GoldPercent = settings.GetValue<int>("DropUponDeath", "GoldPercent");
            DropEquipmentOnDeath = settings.GetValue<bool>("DropUponDeath", "Equipment");
            EquipmentPercent = settings.GetValue<int>("DropUponDeath", "EquipmentPercent");
            DropInventoryOnDeath = settings.GetValue<bool>("DropUponDeath", "Inventory");
            InventoryPercent = settings.GetValue<int>("DropUponDeath", "InventoryPercent");
            DropSpellBook = settings.GetValue<bool>("DropUponDeath", "Spellbook");
            DropQuestItems = settings.GetValue<bool>("DropUponDeath", "QuestItems");
            DropHorse = settings.GetValue<bool>("DropUponDeath", "Horse");
            DropCart = settings.GetValue<bool>("DropUponDeath", "Cart");
            DropCartContents = settings.GetValue<bool>("DropUponDeath", "CartContents");

            LeaveCorpse = settings.GetValue<bool>("DeathOptions", "LeaveCorpse");
            CorpseCanRot = settings.GetValue<bool>("DeathOptions", "CorpseCanRot");
            DaysToRot = settings.GetValue<int>("DeathOptions", "CorpseRotTime");
            MaxCorpses = Mod.GetSettings().GetValue<int>("DeathOptions", "MaxCorpses");
            CanDie = settings.GetValue<bool>("DeathOptions", "CanDie");
            DeathChance = settings.GetValue<int>("DeathOptions", "DeathChance");
            CanLoseLives = settings.GetValue<bool>("DeathOptions", "CanLoseLives");
            MaxLives = settings.GetValue<int>("DeathOptions", "Lives");
            ZeroStatsCauseDeath = settings.GetValue<bool>("DeathOptions", "ZeroStatsCauseDeath");
            LoseXp = settings.GetValue<bool>("DeathOptions", "LoseXp");
            UnconciousMode = settings.GetValue<int>("DeathOptions", "UnconciousMode");
            UnconciousTime = settings.GetValue<int>("DeathOptions", "UnconciousTime") / 3600;
            MaxUnconciousTime = settings.GetValue<int>("DeathOptions", "MaxUnconciousDays");
            EnhanceCorpseVisibility = settings.GetValue<bool>("DeathOptions", "EnhanceCorpseVisibility");
            UpdateCorpsesSize();

            RespawnMode = (RespawnModes)settings.GetValue<int>("DeathOptions", "RespawnMode");
        }

        /// <summary>
        /// Helper for updating the corpses size to match the mod setting value
        /// while minimizing the loss of data.
        /// </summary>
        /// <param name="maxCorpses"></param>
        static bool UpdateCorpsesSize()
        {
            //shrink
            if (MaxCorpses < Corpses.Length)
            {
                //this one is tricky, we want to cull outdated corpses, and then
                //collect remaining corpses in order of newest to oldest so that
                //when we trim the array we lose the older ones.
                CullRottedCorpses();
                Array.Sort(Corpses, Corpses[0]); //not using linq since we have a tiny list and don't care about stability of ordering
                Array.Resize(ref Corpses, MaxCorpses);
                return true;
            }
            //grow
            else if (MaxCorpses > Corpses.Length)
            {
                //this one is easy, just resize the array and allocate new objects
                int old = Corpses.Length;
                Array.Resize<RipCorpseData>(ref Corpses, MaxCorpses);
                for (int i = old; i < MaxCorpses; i++)
                    Corpses[i] = new RipCorpseData();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes corpses that have expired from the serialized list and posts a message to the UI.
        /// </summary>
        static void CullRottedCorpses()
        {
            var currentStockDate = DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
            for (int i = 0; i < Corpses.Length; i++)
            {
                var corpse = Corpses[i];
                if (corpse.Loot != null && currentStockDate - corpse.DropDate > DaysToRot)
                {
                    Log("Corpse[" + i + "] has rotted away.");
                    DelayedHUDText("The equipment you dropped nearby is now long gone.");
                    Corpses[i].Destroyed();
                    //NOTE: this will stop corpses from spawning in but will never destroy any currently in-scene
                }
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="text"></param>
        static void DelayedHUDText(string text)
        {
            GameManager.Instance.StartCoroutine(ShowHUDText(text));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        static IEnumerator ShowHUDText(string message)
        {
            yield return new WaitForSeconds(HUDTextDisplayDelay);
            DaggerfallUI.AddHUDText(message);
        }

        static void CheckForCorpses(DFLocation loc)
        {
            //we need a slight delay here because of how DFU handles loading/starting 'new' games.
            //InternalCheckForCorpses(loc);
            GameManager.Instance.StartCoroutine(DelayedCheckForCorpses(loc));
        }

        static IEnumerator DelayedCheckForCorpses(DFLocation loc)
        {
            yield return new WaitForSeconds(CorpseRespawnDelay);
            InternalCheckForCorpses(loc);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="loc"></param>
        static void InternalCheckForCorpses(DFLocation loc)
        {
            //check to see if any corpses have expired, if so, remove their loot reference
            if (CorpseCanRot)
                CullRottedCorpses();

            //early-out if we have no corpses left
            bool noCorpses = true;
            foreach (var corpse in Corpses)
            {
                if (corpse.Loot != null)
                {
                    noCorpses = false;
                    break;
                }
            }
            if (noCorpses) return;

            //check remaining corpses to see if any are in this region
            var gps = GameManager.Instance.PlayerGPS;
            var player = GameManager.Instance.PlayerEntity;
            bool corpseNearby = false;
            for (int i = 0; i < Corpses.Length; i++)
            {
                var corpse = Corpses[i];
                if (loc.RegionIndex == corpse.Region && loc.MapTableData.MapId == corpse.Map && corpse.Loot != null && !corpse.Spawned)
                {
                    var playerEE = GameManager.Instance.PlayerEnterExit;
                    if (playerEE.IsPlayerInside && corpse.IsInside)
                    {
                        if (IsCorpseInSameBuildingAsPlayer(gps, playerEE, corpse))
                        {
                            if (RestoreCorpse(corpse) != null)
                                corpseNearby = true;
                        }
                    }
                    else if(!playerEE.IsPlayerInside && !corpse.IsInside)
                    {
                        if (RestoreCorpse(corpse) != null)
                            corpseNearby = true;
                    }
                }
            }

            if (corpseNearby)
                DelayedHUDText("If memory serves, your dropped equipment should be in this area.");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="loc"></param>
        static void HandleEnterLocation(DFLocation loc)
        {
            Log("Entering a new location...");
            if (!IsRIPActive)
            {
                Log("RIP is not active. New-location checks will not occur.");
                return;
            }

            SpinWhileLevelLoads(LocationLoadingTimeout, loc);
            CheckForCorpses(loc);
        }

        /// <summary>
        /// Allows us to determine when we enter a tavern so that we can begin tracking if a player has rented a room there.
        /// </summary>
        /// <param name="args"></param>
        static void HandleTransition(TransitionEventArgs args)
        {
            if (!IsRIPActive)
            {
                Log("RIP is not active. New-location checks will not occur.");
                return;
            }

            switch(args.TransitionType)
            {
                case TransitionType.ToBuildingInterior:
                    {
                        Log("Entered a building...");
                        if (args.DaggerfallInterior.BuildingData.BuildingType == DFLocation.BuildingTypes.Tavern)
                        {
                            Log("Entered a tavern...");
                            //there does not appear to be any global notification for when the player rents a room, so instead we
                            //will create a dummy object upon entering a tavern that will pool the player's rented rooms list every frame
                            //and see if it changes.
                            RipTavernPoller poller = null;
                            if (TavernPollerGO == null)
                            {
                                TavernPollerGO = new GameObject("Tavern Poller");
                                poller = TavernPollerGO.AddComponent<RipTavernPoller>();
                            }
                        }
                        else
                        {
                            //just in case we entered a building directly from a tavern... such as through teleporting
                            if (TavernPollerGO != null)
                            {
                                Log("Left a tavern...");
                                GameObject.Destroy(TavernPollerGO);
                                TavernPollerGO = null;
                            }
                        }
                        break;
                    }
                case TransitionType.ToBuildingExterior:
                    {
                        if (TavernPollerGO != null)
                        {
                            Log("Left a tavern...");
                            GameObject.Destroy(TavernPollerGO);
                            TavernPollerGO = null;
                        }
                        break;
                    }
                case TransitionType.ToDungeonInterior:
                    {
                        goto case TransitionType.ToBuildingExterior; //cleanup potential poller due to teleporting
                    }
                case TransitionType.ToDungeonExterior:
                    {
                        goto case TransitionType.ToBuildingExterior; //cleanup potential poller due to teleporting
                    }
            }

            //we are also checking to respawn corpses when moving in or out of buildings and dungeons
            //since exterior and interior spaces are often destroyed in the process. A spawn-guard *should*
            //stop duplicates from being created in most cases... I hope.
            CheckForCorpses(GameManager.Instance.PlayerGPS.CurrentLocation);
        }

        /// <summary>
        /// Returns <c>true</c> if the player and the given corpse is too are in the same building or dungeon
        /// </summary>
        static bool IsCorpseInSameBuildingAsPlayer(PlayerGPS gps, PlayerEnterExit playerEE, RipCorpseData corpse)
        {
            if (!playerEE.IsPlayerInside || !corpse.IsInside) return false;
            if (gps.CurrentMapPixel.X != corpse.WorldPosX || gps.CurrentMapPixel.Y != corpse.WorldPosY)
                return false;

            //afaik there can only be one dungeon per map pixel so we should be good enough with this
            if (corpse.DropLocation.insideDungeon && (playerEE.IsPlayerInsideDungeon || playerEE.IsPlayerInsideDungeonCastle))
                return true;

            //compare building locations
            var bdd = playerEE.BuildingDiscoveryData;
            if (corpse.Loot != null && corpse.DropLocation != null && corpse.DropLocation.insideBuilding)
            {
                if (bdd.buildingKey == corpse.DropLocation.buildingDiscoveryData.buildingKey)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Handler for the default global death-event.
        /// </summary>
        static void HandleDeath(object sender, EventArgs args)
        {
            Log("Detected death event!");
            if (!IsRIPActive)
            {
                Log("RIP is not active. Normal death event has been triggered.");
                GameManager.Instance.StateManager.PlayerDeath_OnPlayerDeathHandler(sender, args);
                return;
            }

            ProcessDeathEvent(sender, args);
        }

        /// <summary>
        /// Processes the actual death/respawn logic.
        /// </summary>
        static void ProcessDeathEvent(object sender, EventArgs args)
        {
            //some madlad has sent an external message letting us know the next death is legit... but just this once
            if(NextDeathPassthrough)
            {
                NextDeathPassthrough = false;
                GameManager.Instance.StateManager.PlayerDeath_OnPlayerDeathHandler(sender, args);
                return;
            }


            var player = GameManager.Instance.PlayerEntity;
            var playerEE = GameManager.Instance.PlayerEnterExit;
            var worldPos = GameManager.Instance.PlayerGPS.CurrentMapPixel;


            //if any stat hits zero, that normally triggers a death event. do we proceed as normal?
            //stats will be fixed during respawn by curing all ill effects
            if (ZeroStatsCauseDeath)
            {
                for (int i = 0; i < DaggerfallStats.Count; i++)
                {
                    if(player.Stats.GetLiveStatValue((DFCareer.Stats)i) < 1)
                    {
                        //die like normal
                        GameManager.Instance.StateManager.PlayerDeath_OnPlayerDeathHandler(sender, args);
                        return;
                    }
                }
            }

            if (CanLoseLives)
            {
                if (LivesLeft <= 1)
                {
                    //die as normal
                    GameManager.Instance.StateManager.PlayerDeath_OnPlayerDeathHandler(sender, args);
                    return;
                }
                else
                {
                    //lose a life and respawn
                    LivesLeft--;
                }
            }
            else
            {
                //check if we just die like normal
                if (CanDie)
                {
                    if (Dice100.SuccessRoll(DeathChance))
                    {
                        //die like normal
                        GameManager.Instance.StateManager.PlayerDeath_OnPlayerDeathHandler(sender, args);
                        return;
                    }
                }
            }
            //Reset player state for the frame.
            //It's vital that we do this right away since the PlayerDeath component updates
            //every frame and after a fixed time of 2 seconds it will reset the game if we
            //are still in a 'deathInProgress' state.
            GameManager.Instance.PlayerDeath.ClearDeathAnimation(); //removes the deathInProgress flag
            GameManager.Instance.PlayerMotor.CancelMovement = true;
            InputManager.Instance.ClearAllActions();

            DaggerfallLoot corpseLoot = LeaveCorpse ? GenerateCorpseAndDropGear(player, worldPos) : null;
            Respawn(player, playerEE, worldPos); //this can have a delay due to spinning in a tight loop, waiting for the destination to load

        }

        /// <summary>
        /// Creates a lootable object in the scene and moves player inventory into it based on mod settings.
        /// </summary>
        static DaggerfallLoot GenerateCorpseAndDropGear(PlayerEntity player, DFPosition worldPos)
        {
            Log("Dropping items, gear, and gold...");
            var loot = GameObjectHelper.CreateDroppedLootContainer(GameManager.Instance.PlayerObject, DaggerfallUnity.NextUID);
            loot.ContainerImage = InventoryContainerImages.Corpse3;
            loot.ContainerType = LootContainerTypes.CorpseMarker;
            loot.playerOwned = false;
            loot.customDrop = false; //stops the savemanager from thinking it should be saved

            if (DropGoldOnDeath)
            {
                if (GoldPercent > 100) GoldPercent = 100;
                if (GoldPercent < 0) GoldPercent = 0;

                int goldToDrop = Mathf.CeilToInt(player.GoldPieces * (GoldPercent / 100.0f));

                if (goldToDrop > 0)
                {
                    player.GoldPieces -= goldToDrop;
                    loot.Items.AddItem(ItemBuilder.CreateGoldPieces(goldToDrop));
                }
            }

            //drop equipped gear
            if (DropEquipmentOnDeath)
            {
                foreach (var item in player.ItemEquipTable.EquipTable)
                {
                    if (item != null && item.IsEquipped)
                    {
                        if (Dice100.SuccessRoll(EquipmentPercent))
                        {
                            item.UnequipItem(player);
                            loot.Items.Transfer(item, player.Items);
                        }
                    }
                }
            }

            //drop non-equipped gear
            if (DropInventoryOnDeath)
            {
                //transfer all items to loot and then filter out the quest items and return them back to the player
                loot.Items.TransferAll(player.Items);

                if (!DropSpellBook)
                {
                    //let's not be too cruel, give back the spellbook if we find it
                    foreach (var book in loot.Items.SearchItems(ItemGroups.MiscItems))
                    {
                        if (book.shortName.ToUpper() == "SPELLBOOK")
                            player.Items.Transfer(book, loot.Items);
                    }
                }

                if (!DropQuestItems)
                {
                    foreach (var questItem in loot.Items.SearchItems(ItemGroups.QuestItems))
                    {
                        if (Dice100.SuccessRoll(InventoryPercent))
                        {
                            player.Items.Transfer(questItem, loot.Items);
                        }
                    }
                }

                if (!DropHorse || !DropCart)
                {
                    foreach(var transport in loot.Items.SearchItems(ItemGroups.Transportation))
                    {
                        string shortName = transport.shortName.ToUpper();
                        if (shortName.Contains("HORSE") && !DropHorse)
                            player.Items.Transfer(transport, loot.Items);

                        if (shortName.Contains(" CART") && !DropCart)
                            player.Items.Transfer(transport, loot.Items);
                    }
                }

            }

            //TODO: reset EXP
            if (LoseXp)
            {
                //TODO:
            }

            //if there is nothing that was dropped, remove the loot container
            if (loot.Items.Count < 1)
            {
                GameObject.Destroy(loot.gameObject);
                return null;
            }

            //store both the loot itself and where it has dropped so that we can re-create it later upon re-entering this location
            loot.name = "Player Corpse";
            var slc = loot.GetComponent<SerializableLootContainer>();
            var corpse = new RipCorpseData()
            {
                WorldPosX = worldPos.X,
                WorldPosY = worldPos.Y,
                Region = GameManager.Instance.PlayerGPS.CurrentRegionIndex,
                Map = GameManager.Instance.PlayerGPS.CurrentMapID,
                Loot = slc.GetSaveData() as LootContainerData_v1,
            };
            

            //now we strip the SerializableLootContainer so that it doesn't get saved with
            //the scene, reason being is that we'll re-create it ourself during loading
            GameObject.Destroy(slc);

            int corpseIndex = OldestCorpseIndex;
            corpse.Loot.stockedDate = corpseIndex;
            corpse.DropDate = DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
            corpse.DropLocation = GetPlayerLocationForAnchor();

            //link the lootablecontainer to the cached loot data in this class
            //var poller = loot.gameObject.AddComponent<RipCorpseLootPoller>();
            //poller.LinkCorpse(corpse);
            GameObject.Destroy(loot.gameObject);
            loot = null;

            //write corpse data back aa final step
            Corpses[corpseIndex] = corpse;
            return loot;
        }

        /// <summary>
        /// Helper for ensuring that spawned corpses appear at the correct terrain level in exterior scenes.
        /// Only works for interior scenes.
        /// </summary>
        static bool RepositionCorpse(DaggerfallLoot corpseObject, PlayerPositionData_v1 corpsePos)
        {
            var tSampler = DaggerfallUnity.Instance.TerrainSampler;
            corpsePos.terrainSamplerName = tSampler.ToString();
            corpsePos.terrainSamplerVersion = tSampler.Version;
            if (!corpsePos.insideBuilding && corpsePos.insideDungeon)
            {
                if (MatchPositionToTerrainHeight(corpseObject.transform, 1, 0.6f))
                {
                    //update corpse data so that it spawns correctly next time
                    corpsePos.position = corpseObject.transform.position;
                }
                else
                {
                    int corpseIndex = int.Parse(corpseObject.entityName);
                    Log("Corpse[" + corpseIndex + "] has rotted away.");
                    DelayedHUDText("The equipment you dropped nearby is now long gone.");
                    Corpses[corpseIndex].Destroyed();
                    return false;
                }
            }


            return true;
        }

        /// <summary>
        /// Resets the players stats, teleports them to the appropriate location and displays an RP message to let them know what happened.
        /// </summary>
        static void Respawn(PlayerEntity player, PlayerEnterExit playerEE, DFPosition worldPos)
        {
            Log("Post-death respawning...");

            OnPrePlayerRespawn?.Invoke();

            //reset health state and respawn at a new location
            GameManager.Instance.PlayerEffectManager.CureAll();
            player.SetHealth(player.MaxHealth);
            player.SetMagicka(player.MaxMagicka);
            player.SetFatigue(player.MaxFatigue);

            var deathLocation = GameManager.Instance.PlayerGPS.CurrentLocation; //cache place of death
            var result = TeleportPlayerToRespawnPos(player, playerEE, worldPos.X, worldPos.Y, RespawnData.AnchorPos);
            if (result == RespawnResults.Failure) return;
            var msgBox = new DaggerfallMessageBox(DaggerfallUI.UIManager);
            msgBox.SetText(GetRespawnMessage(result));
            msgBox.ClickAnywhereToClose = true;
            SpinWhileLevelLoads(LocationLoadingTimeout); //this causes the game to hang... need to find some way to avoid this...
            msgBox.Show();

            //loot containers do not appear to remain in the scene if we teleport back to the same place. so
            //we'll compare the before and after locations to see if we are in the same location. if we are,
            //manually trigger a respawn of lootable corpses.
            var currLocation = GameManager.Instance.PlayerGPS.CurrentLocation;
            if (deathLocation.MapTableData.MapId == currLocation.MapTableData.MapId &&
               deathLocation.RegionIndex == currLocation.RegionIndex)
            {
                HandleEnterLocation(currLocation);
            }

            OnPostPlayerRespawn?.Invoke();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        static string[] GetRespawnMessage(RespawnResults result)
        {

            string[] textLines = new string[10];
            switch (result)
            {
                case RespawnResults.FamiliarTavern:
                    {
                        textLines = new string[] {
                            "You collapse to the ground in a heap. Everything around you fades to black.",
                            "Eventually you regain conciousness for but a moment and in that time you see a",
                            "blurry figure kneeling toward you.",
                            "",
                            "Some time later you come to your senses. With a jolt you stand up and find",
                            "yourself in a familiar tavern.",
                            "",
                            "Someone must have recognized you as a recent patron of this place and brought you back.",
                            "",
                            "",//dummy space for lives counter. i'd use a second message box but that seems to fuck a lot of thing sup
                            };
                        break;
                    }
                case RespawnResults.RandomTavern:
                    {
                        textLines = new string[] {
                            "You collapse to the ground in a heap. Everything around you fades to black.",
                            "Eventually you regain conciousness for but a moment and in that time you see a",
                            "blurry figure kneeling toward you.",
                            "",
                            "As you slowly come to you find yourself in an unfamiliar tavern.",
                            "",
                            "Rescued! But by whom?",
                            "",
                            "",//dummy space for lives counter. i'd use a second message box but that seems to fuck a lot of thing sup
                            };
                        break;
                    }
                case RespawnResults.Wilderness:
                    {
                        textLines = new string[] {
                            "You collapse to the ground in a heap. Everything around you fades to black.",
                            "Eventually you regain conciousness for but a moment and in that time you see a",
                            "blurry figure kneeling toward you.",
                            "",
                            "As your vision clears you find yourself leaned against a rock. A long dead fire nearby.",
                            "",
                            "How did you get here?",
                            "",
                            "",//dummy space for lives counter. i'd use a second message box but that seems to fuck a lot of thing sup
                            };
                        break;
                    }
            }

            if (CanLoseLives)
                textLines[9] = "(You have " + LivesLeft + ((LivesLeft > 1) ? " lives" : " life") + " remaining)";

            return textLines;
        }

        /// <summary>
        /// Teleports the player back to the last tavern they had stayed in or to the default
        /// starting location for a new game if they have never rented a room.
        /// </summary>
        /// <param name="player"></param>
        /// <returns><c>true</c> if the player has been teleported back to a tavern, <c>false</c> otherwise.</returns>
        static RespawnResults TeleportPlayerToRespawnPos(PlayerEntity player, PlayerEnterExit playerEE, int mapX, int mapY, PlayerPositionData_v1 respawnPos)
        {
            //note that we already called the cure function BEFORE this point but we will be calling
            //it again after passing time just in case something weird happens
            Action cure = GameManager.Instance.PlayerEffectManager.CureAll; //I'm lazy and don't feel like typing/copy-pasting this out every time

            switch (RespawnMode)
            {
                case RespawnModes.LastTavern:
                    {
                        if (RespawnData.RespawnTypeId == RespawnType.RandomRoom)
                        {
                            TeleportPlayerToPosition(playerEE, RespawnData.AnchorPos);
                            PassTime(RespawnData.WorldPosX, RespawnData.WorldPosY);
                            cure();
                            return RespawnResults.FamiliarTavern;
                        }
                        else goto case RespawnModes.RandomTavern;
                    }
                case RespawnModes.LastNonExpiredtavern:
                    {
                        var lastRentalRespawn = GetRespawnDataForLastRentedRoom(player);
                        if (lastRentalRespawn.HasValue)
                        {
                            var respawn = lastRentalRespawn.Value;
                            TeleportPlayerToPosition(playerEE, respawn.AnchorPos);
                            PassTime(respawn.WorldPosX, respawn.WorldPosY);
                            cure();
                            return RespawnResults.FamiliarTavern;
                        }
                        else goto case RespawnModes.RandomTavern;
                    }
                case RespawnModes.RandomTavern:
                    {
                        //not yet implemented!
                        goto default;
                    }
                default:
                    {
                        //last ditch effort is to respawn them at default outdoor starting location
                        int respawnX = DaggerfallUnity.Settings.StartCellX;
                        int respawnY = DaggerfallUnity.Settings.StartCellY;
                        PassTime(respawnX, respawnY);
                        playerEE.EnableExteriorParent();
                        StreamingWorld streamingWorld = GameObject.FindObjectOfType<StreamingWorld>();
                        streamingWorld.TeleportToCoordinates(respawnX, respawnY);
                        streamingWorld.SetAutoReposition(StreamingWorld.RepositionMethods.RandomStartMarker, Vector3.zero);
                        streamingWorld.suppressWorld = false;
                        if (!MatchPositionToTerrainHeight(GameManager.Instance.PlayerMotor.transform, GameManager.Instance.PlayerMotor.controller.height))
                        {
                            //we're fucked...
                            DaggerfallUI.AddHUDText("The gods have seen fit to curse you!");
                            Log("Could not locate valid position in wilderness. Killing player as the default option.");
                            GameManager.Instance.StateManager.PlayerDeath_OnPlayerDeathHandler(null, null);
                            return RespawnResults.Failure;
                        }
                        return RespawnResults.Wilderness;
                    }
            }
        }

        /// <summary>
        /// Restores a previously spawned loot drop. The corpse index is supplied to a component
        /// </summary>
        static DaggerfallLoot RestoreCorpse(RipCorpseData corpseData)
        {
            if (corpseData.Loot == null || corpseData.Spawned) return null;

            Log("Respawning corpse");
            var loot = GameObjectHelper.CreateDroppedLootContainer(GameManager.Instance.PlayerObject, DaggerfallUnity.NextUID);
            SerializableLootContainer slc = loot.GetComponent<SerializableLootContainer>();
            var origContext = corpseData.Loot.worldContext;
            corpseData.Loot.loadID = loot.LoadID; //these need to match or it won't assign the loot
            slc.RestoreSaveData(corpseData.Loot);
            loot.name = "Player Corpse";
            loot.ContainerImage = InventoryContainerImages.Corpse3;
            loot.ContainerType = LootContainerTypes.CorpseMarker;
            loot.playerOwned = false;
            loot.customDrop = false; //stops the savemanager from thinking it should be saved


            //RestoreSaveData does not account for loot being in dungeons so we need to fix that here
            if (origContext == WorldContext.Dungeon)
            {
                loot.transform.SetParent(GameManager.Instance.DungeonParent.transform, true);
                RestoreDungeonPositionHandler(loot, corpseData.Loot, origContext);
            }

            //now we strip the SerializableLootContainer so that it doesn't get saved with
            //the scene, reason being is that we'll re-create it ourself during loading
            GameObject.Destroy(slc);

            //correct any height-issues due to terrain updates (or really anything at all)
            //This needs to be done via a couroutine because loading the terrain likely happens AFTER this
            SpinWhileLevelLoads(LocationLoadingTimeout);
            if(!RepositionCorpse(loot, GetPlayerLocationForAnchor()))
            {
                GameObject.Destroy(loot.gameObject);
                loot = null;
            }
            //link the lootablecontainer to the cached loot data in this class
            var poller = loot.gameObject.AddComponent<RipCorpseLootPoller>();
            poller.LinkCorpse(corpseData);

            if (EnhanceCorpseVisibility)
            {
                var particles = GameObject.Instantiate<GameObject>(ParticlesPrefab);
                particles.transform.SetParent(loot.transform);
                particles.transform.localPosition = Vector3.zero;
            }
            return loot;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="loot"></param>
        /// <param name="data"></param>
        /// <param name="lootContext"></param>
        static void RestoreDungeonPositionHandler(DaggerfallLoot loot, LootContainerData_v1 data, WorldContext lootContext)
        {
            // If loot context matches serialized world context then loot was saved after floating y change
            // Can simply restore local position relative to parent interior
            if (lootContext == data.worldContext)
            {
                loot.transform.localPosition = data.localPosition;
                return;
            }

            // Otherwise we need to migrate a legacy interior position to floating y
            if (GameManager.Instance.PlayerEnterExit.LastInteriorStartFlag)
            {
                // Loading interior uses serialized absolute position (as interior also serialized this way)
                loot.transform.position = data.currentPosition;
            }
            else
            {
                // Transition to interior must offset serialized absolute position by floating y compensation
                loot.transform.position = data.currentPosition + GameManager.Instance.StreamingWorld.WorldCompensation;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeout"></param>
        static bool SpinWhileLevelLoads(float timeout)
        {
            return SpinWhileLevelLoads(timeout, GameManager.Instance.PlayerGPS.CurrentLocation);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeout"></param>
        static bool SpinWhileLevelLoads(float timeout, DFLocation currLocation)
        {

            Log($"Spinning for {timeout} seconds while location loads...");
            var timeStart = Time.realtimeSinceStartup;
            while (!currLocation.Loaded)
            {
                if (Time.realtimeSinceStartup - timeStart > timeout)
                {

                    Log("...spinning timed out.");
                    return false;
                }
            }

            Log("... done spinning. Loading complete.");
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="destX"></param>
        /// <param name="destY"></param>
        static void PassTime(int destX, int destY)
        {
            //determine time passed
            switch (UnconciousMode)
            {
                case UnconciousModeFixed:
                    {
                        DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.RaiseTime(UnconciousTime); //add 12 hours to the clock
                        break;
                    }
                case UnconciousModeDistance:
                    {
                        TravelTimeCalculator ttc = new TravelTimeCalculator();
                        int minutes = ttc.CalculateTravelTime(new DFPosition(destX, destY), true, false, true, true, false);
                        int totalTime = Mathf.Max(minutes * 60, 3600);//minimum of one hour
                        totalTime = Mathf.Min(totalTime, MaxUnconciousTime * 86400); //max is limited by settings
                        DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.RaiseTime(totalTime); 
                        break;
                    }
                default:
                    {
                        DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.RaiseTime(UnconciousTime); //add 12 hours to the clock
                        break;
                    }
            }
        }
        #endregion


        #region Public Methods
        /// <summary>
        /// Helper for making snytax highlighted text for debugging.
        /// </summary>
        /// <param name="text"></param>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Log(string text)
        {
            //posting as a warning because it's easier to filter in unity's console
            Debug.LogWarning("<color=green>" + text + "</color>");
        }

        /// <summary>
        /// Scans up and down for a great distance and makes any needed adjustments
        /// to ensure the player will safely start on ground.
        /// Without this it's possible that they might spawn high up in the air or even underground!
        /// </summary>
        /// <param name="trans"></param>
        public static bool MatchPositionToTerrainHeight(Transform objToMove, float colliderHeight, float placementFudge = 0.65f, float scanDist = 3000f)
        {
            const float fudge = 2; //needed because we want to ensure the raycast actually starts outside of the object it is scanning for.

            //try scanning down first
            Ray ray = new Ray(objToMove.position + (Vector3.up * fudge), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, scanDist))
            {
                // Position player at hit position plus just over half controller height up
                objToMove.position = hit.point + Vector3.up * (colliderHeight * placementFudge);
                return true;
            }

            //no luck? how 'bout up?
            bool cachedBackfaceSetting = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            ray = new Ray(objToMove.position + (Vector3.down * fudge), Vector3.up);
            if (Physics.Raycast(ray, out hit, scanDist))
            {
                Physics.queriesHitBackfaces = cachedBackfaceSetting;
                // Position player at hit position plus just over half controller height up
                objToMove.position = hit.point + Vector3.up * (colliderHeight * placementFudge);
                return true;
            }
            Physics.queriesHitBackfaces = cachedBackfaceSetting;
            return false;
        }

        /// <summary>
        /// Forces the death event processing to trigger, causing lives lost, loot to drop, and death or respawning.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        public static void ForceDeathEvent(object sender, EventArgs args)
        {
            ProcessDeathEvent(sender, args);
        }

        /// <summary>
        /// Checks to see if the current GPS location of the player would have any previously
        /// dropped corpses, and spawns them if so. Also causes expired corpses to disappear.
        /// </summary>
        /// <param name="locationEntered"></param>
        public static void ForceCheckForCorpses()
        {
            CheckForCorpses(GameManager.Instance.PlayerGPS.CurrentLocation);
        }

        /// <summary>
        /// Checks to see if the given location of the would have any previously
        /// dropped corpses, and spawns them if so. Also causes expired corpses to disappear.
        /// </summary>
        /// <param name="locationEntered"></param>
        public static void ForceCheckForCorpses(DFLocation loc)
        {
            CheckForCorpses(loc);
        }

        /// <summary>
        /// This is meant to be called exzternally from the RipTavernPoller when a rental is detected.
        /// It will store the current location as the respawn point.
        /// </summary>
        public static void KeepLocationAsRespawnPoint(RespawnType respawnType)
        {
            //store last location rented, regardless of rental state.
            //This way we always have a fallback where we can port to the last know rental
            //even if it's not valid anymore.
            var worldPos = GameManager.Instance.PlayerGPS.CurrentMapPixel;
            RespawnData.AnchorPos = GetPlayerLocationForAnchor();
            RespawnData.WorldPosX = worldPos.X;
            RespawnData.WorldPosY = worldPos.Y;
            RespawnData.RespawnTypeId = respawnType;

            //also add to rental list for active tracking later
            //we'll assume the lastest room in the list is the newest one
            var player = GameManager.Instance.PlayerEntity;
            var lastRoom = GetLastValidRoom(player);
            string roomId = DaggerfallInterior.GetSceneName(lastRoom.mapID, lastRoom.buildingKey);

            //technically we can't ever rent more time in a tavern but just in case that feature were ever added, this will account for that fact.
            RespawnList[roomId] = new RipRespawnData()
            {
                AnchorPos = GetPlayerLocationForAnchor(),
                WorldPosX = worldPos.X,
                WorldPosY = worldPos.Y,
                RespawnTypeId = respawnType,
            };
        }

        /// <summary>
        /// Helper for getting the anchor pos that was stored and associated with the last rented room
        /// that is still available.
        /// </summary>
        /// <returns>The anchor pos for the last rented room still available or null if there are no rooms left.</returns>
        public static RipRespawnData? GetRespawnDataForLastRentedRoom(PlayerEntity player)
        {
            var lastRoom = GetLastValidRoom(player);
            if (lastRoom == null)
                 return null;

            string roomId = DaggerfallInterior.GetSceneName(lastRoom.mapID, lastRoom.buildingKey);
            if (!RespawnList.TryGetValue(roomId, out var respawnData))
                return null;

            return respawnData;
        }

        /// <summary>
        /// Returns the last
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public static RoomRental_v1 GetLastValidRoom(PlayerEntity player)
        {
            //might at well update our lists of rentals while we are at it
            player.RemoveExpiredRentedRooms();
            RemoveExpiredRespawns(player);
            return player.RentedRooms.Count > 0 ? player.RentedRooms[player.RentedRooms.Count - 1] : null;
        }

        static List<string> RemovalKeys = new List<string>(10);
        /// <summary>
        /// Helper for removing respawns for tavern rooms that have expired.
        /// </summary>
        static void RemoveExpiredRespawns(PlayerEntity player)
        {
            RemovalKeys.Clear();
            foreach(var kvp in RespawnList)
            {
                var tavernInfo = GetLocationOfScene(kvp.Key);
                if (tavernInfo == null)
                    RemovalKeys.Add(kvp.Key);

                var room = player.GetRentedRoom(tavernInfo.First, tavernInfo.Second);
                if (room == null || PlayerEntity.GetRemainingHours(room) < 1)
                    RemovalKeys.Add(kvp.Key);
            }

            foreach (var key in RemovalKeys)
                RespawnList.Remove(key);
        }

        static Regex reg = new Regex(@"DaggerfallInterior \[MapID=(\d+), BuildingKey=(\d+)\]");
        /// <summary>
        /// Helper for extracting location indicies from a scene string built using DaggerfallInterior.GetSceneName().
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        static DaggerfallWorkshop.Utility.Tuple<int, int> GetLocationOfScene(string scene)
        {
            var match = reg.Match(scene);
            var groups = match.Groups;
            if (groups == null || groups.Count != 3)
                return null;

            //note: group[0] is the original regex string
            return new DaggerfallWorkshop.Utility.Tuple<int, int>(int.Parse(groups[1].Value), int.Parse(groups[2].Value));
        }

        /// <summary>
        /// Returns the PlayerPositionData for the player's current location.
        /// </summary>
        /// <returns></returns>
        public static PlayerPositionData_v1 GetPlayerLocationForAnchor()
        {
            var playerEE = GameManager.Instance.PlayerEnterExit;
            var playerS = playerEE.GetComponent<SerializablePlayer>();
            PlayerPositionData_v1 newAnchorPosition = playerS.GetPlayerPositionData();

            newAnchorPosition.exteriorDoors = playerEE.ExteriorDoors;
            newAnchorPosition.buildingDiscoveryData = playerEE.BuildingDiscoveryData;
            return newAnchorPosition;
        }

        /// <summary>
        /// Removes the cached information for the corpse at the given index.
        /// </summary>
        /// <param name="corpseIndex"></param>
        public static void RemoveCorpseAt(int corpseIndex)
        {
            //allow errors to happen when testing in the editor, but let's safeguard it in release
#if !UNITY_EDITOR
            if (corpseIndex >= Corpses.Length) return;
#endif
            Corpses[corpseIndex].Destroyed();
        }
        #endregion


        #region Borrowed code from MagicEffects.Teleport

        /// <summary>
        /// Teleports the player to the supplied world position.
        /// Ripped from MagicEffects.Teleport
        /// </summary>
        public static void TeleportPlayerToPosition(PlayerEnterExit playerEnterExit, PlayerPositionData_v1 teleportDest)
        {
            var serializablePlayer = playerEnterExit.GetComponent<SerializablePlayer>();

            // Is player in same interior as anchor?
            if (IsSameInterior(playerEnterExit, serializablePlayer, teleportDest))
            {
                // Just need to move player
                serializablePlayer.RestorePosition(teleportDest);
            }
            else
            {
                // When teleporting to interior anchor, restore world compensation height early before initworld
                // Ensures exterior world level is aligned with building height at time of anchor
                // Only works with floating origin v3 saves and above with both serialized world compensation and context
                if (teleportDest.worldContext == WorldContext.Interior)
                    GameManager.Instance.StreamingWorld.RestoreWorldCompensationHeight(teleportDest.worldCompensation.y);
                else
                    GameManager.Instance.StreamingWorld.RestoreWorldCompensationHeight(0);

                // Cache scene before departing
                if (!playerEnterExit.IsPlayerInside)
                    SaveLoadManager.CacheScene(GameManager.Instance.StreamingWorld.SceneName);      // Player is outside
                else if (playerEnterExit.IsPlayerInsideBuilding)
                    SaveLoadManager.CacheScene(playerEnterExit.Interior.name);                      // Player inside a building
                else // Player inside a dungeon
                    playerEnterExit.TransitionDungeonExteriorImmediate();

                // Need to load some other part of the world again - player could be anywhere
                PlayerEnterExit.OnRespawnerComplete += HandleRespawnerComplete;
                playerEnterExit.RestorePositionHelper(teleportDest, false, true);

                // Restore building summary data
                if (teleportDest.insideBuilding)
                    playerEnterExit.BuildingDiscoveryData = teleportDest.buildingDiscoveryData;

                // When moving anywhere other than same interior trigger a fade so transition appears smoother
                DaggerfallUI.Instance.FadeBehaviour.FadeHUDFromBlack();
            }
        }

        /// <summary>
        /// Checks if player is in same building or dungeon interior as anchor
        /// </summary>
        /// <returns></returns>
        static bool IsSameInterior(PlayerEnterExit playerEnterExit, SerializablePlayer serializablePlayer, PlayerPositionData_v1 posToCheck)
        {
            // Reject if outside or anchor not set
            if (!playerEnterExit.IsPlayerInside || posToCheck == null)
                return false;

            // Test depends on if player is inside a building or a dungeon
            if (playerEnterExit.IsPlayerInsideBuilding && posToCheck.insideBuilding)
            {
                // Compare building key
                if (posToCheck.buildingDiscoveryData.buildingKey == playerEnterExit.BuildingDiscoveryData.buildingKey)
                {
                    // Also compare map pixel, in case we're unlucky https://forums.dfworkshop.net/viewtopic.php?f=24&t=2018
                    DFPosition anchorMapPixel = DaggerfallConnect.Arena2.MapsFile.WorldCoordToMapPixel(posToCheck.worldPosX, posToCheck.worldPosZ);
                    DFPosition playerMapPixel = GameManager.Instance.PlayerGPS.CurrentMapPixel;
                    if (anchorMapPixel.X == playerMapPixel.X && anchorMapPixel.Y == playerMapPixel.Y)
                        return true;
                }
            }
            else if (playerEnterExit.IsPlayerInsideDungeon && posToCheck.insideDungeon)
            {
                // Compare map pixel of dungeon (only one dungeon per map pixel allowed)
                DFPosition anchorMapPixel = DaggerfallConnect.Arena2.MapsFile.WorldCoordToMapPixel(posToCheck.worldPosX, posToCheck.worldPosZ);
                DFPosition playerMapPixel = GameManager.Instance.PlayerGPS.CurrentMapPixel;
                if (anchorMapPixel.X == playerMapPixel.X && anchorMapPixel.Y == playerMapPixel.Y)
                {
                    GameManager.Instance.PlayerEnterExit.PlayerTeleportedIntoDungeon = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ripped directly from MagicEffects.Teleport
        /// </summary>
        static void HandleRespawnerComplete()
        {
            var playerEnterExit = GameManager.Instance.PlayerEnterExit;
            var serializablePlayer = playerEnterExit.GetComponent<SerializablePlayer>();
            var anchorPosition = RespawnData.AnchorPos;

            // Restore final position and unwire event
            serializablePlayer.RestorePosition(anchorPosition);
            PlayerEnterExit.OnRespawnerComplete -= HandleRespawnerComplete;

            // Set "teleported into dungeon" flag when anchor is inside a dungeon
            GameManager.Instance.PlayerEnterExit.PlayerTeleportedIntoDungeon = anchorPosition.insideDungeon;

            // Restore scene cache on arrival
            if (!playerEnterExit.IsPlayerInside)
                SaveLoadManager.RestoreCachedScene(GameManager.Instance.StreamingWorld.SceneName);      // Player is outside
            else if (playerEnterExit.IsPlayerInsideBuilding)
                SaveLoadManager.RestoreCachedScene(playerEnterExit.Interior.name);                      // Player inside a building

        }
        #endregion

        public static void ScanRegions()
        {
            const int maxRegions = 62;

            for (int n = 0; n < maxRegions; n++)
            {
                DFRegion regionInfo = DaggerfallUnity.Instance.ContentReader.MapFileReader.GetRegion(n);
                if (regionInfo.LocationCount <= 0) // Add the if-statements to keep "invalid" regions from being put into grab-bag, also use this for some settings.
                    continue;
                if (n == 31) // Index for "High Rock sea coast" or the "region" that holds the location of the two player boats, as well as the Mantellan Crux story dungeon.
                    continue;
                if (n == 61) // Index for "Cybiades" the isolated region that has only one single location on the whole island, that being a dungeon.
                    continue;

                for(int i = 0; i < regionInfo.LocationCount; i++)
                {
                    DFRegion.RegionMapTable mapTable = regionInfo.MapTable[i];
                    DFLocation loc = DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation(n, i);


                    for (int b = 0; b < loc.Exterior.BuildingCount; b++)
                    {
                        var building = loc.Exterior.Buildings[b];
                        if(building.BuildingType == DFLocation.BuildingTypes.Tavern)
                        {
                            //this is our place!
                            //building.
                        }
                    }
                }
            }
        }
    }

}
