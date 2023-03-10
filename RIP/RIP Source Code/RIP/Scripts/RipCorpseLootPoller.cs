using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Serialization;
using static RIP.RipDeathHandler;

namespace RIP
{
    /// <summary>
    /// Attach to a DaggerfallLoot object. Continually polls the sate of the loot container, checking for changes
    /// in its inventory. These changes are synced back to RipDeathHandler's matching cached loot data that is
    /// serialized and managed by the mani mod logic.
    /// 
    /// NOTE: This method totally sucks but without reliable means of tracking changes to loot containers via
    /// event handlers, this is the best we're gonna get.
    /// </summary>
    [RequireComponent(typeof(DaggerfallLoot))]
    public class RipCorpseLootPoller : MonoBehaviour
    {
        //we have to do it this way because daggerfall moves too quick and can attempt to do things before objects have had Awake called
        DaggerfallLoot _Loot;
        DaggerfallLoot Loot
        {
            get => _Loot == null ? _Loot = GetComponent<DaggerfallLoot>() : _Loot;
        }
        public RipCorpseData Corpse;


        private void Awake()
        {
            //Loot.OnInventoryClose += HandleContainerClose();
            RipDeathHandler.OnPrePlayerRespawn += HandlePlayerRespawn;
        }

        private void OnDisable()
        {
            //we have to also handle this here because dungeons are kinda weird
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            RipDeathHandler.Log("Removing Corpse");
            //Loot.OnInventoryClose -= HandleContainerClose();
            RipDeathHandler.OnPrePlayerRespawn -= HandlePlayerRespawn;
            if(Corpse != null)
                Corpse.Spawned = false; //allow this item to respawn if this object is re-created later
            RemoveLinkToCachedData(); //safety check to ensure no matter what, the link is removed
        }

        void HandlePlayerRespawn()
        {
            Destroy(gameObject);
        }

        void HandleContainerClose()
        {
            RemoveLinkToCachedData();
        }

        /// <summary>
        /// Removes any link this container has back to the cached data in the mod
        /// and then ensures this object will be destroyed.
        /// </summary>
        void RemoveLinkToCachedData()
        {
            if (Loot != null && Loot.Items.Count == 0)
            {
                if(Corpse != null) Corpse.Destroyed();
                Corpse = null;
                enabled = false;
                _Loot = null;
                Destroy(gameObject);
                return;
            }
        }

        /// <summary>
        /// Called externally by the mod to link the cached corpse data to this container
        /// so that their inventories can be syncronised.
        /// </summary>
        /// <param name="corpse"></param>
        public void LinkCorpse(RipCorpseData corpse)
        {
            Corpse = corpse;
            Corpse.Spawned = true;
            corpse.Loot.items = Loot.Items.SerializeItems();
            enabled = true;
        }

        /// <summary>
        /// 
        /// </summary>
        private void Update()
        {
            SyncCorpseToLoot();
        }

        /// <summary>
        /// Compares and ItemCollection with the contents of the items at the given corpseIndex. If the item count
        /// differs it copies the contents of the ItemCollection into the corpse loot.
        ///
        /// HACK ALERT: This is a fast and dirty method for keeping serialized corpse data in sync with their assoaciated
        /// LootContainers. It's far from perfect. In fact it sucks. It's slow and it's massively prone to all kinds of expoits.
        /// </summary>
        /// <param name="corpseIndex"></param>
        /// <param name="lootItems"></param>
        public void SyncCorpseToLoot()
        {
            if (Corpse.Loot == null)
            {
                Destroy(gameObject);
                return;
            }

            //if the container is empty, sync the cache, remove the link entirely and then destroy this lootable container
            if(Loot.Items.Count == 0)
            {
                //Removed because it causes issues if players try to put stock back.
                //This means that corpses will remain in the scene until their parent space
                //is removed but at least players have the chance to swap items around
                //while trying to get their loot back
                /*
                Corpse.Destroyed();
                Corpse = null;
                enabled = false;
                Destroy(gameObject);
                return;
                */

                //opted to at least disable the corpse visibility effect since we can't outright destroy the container itself
                var particles = gameObject.GetComponentInChildren<ParticleSystem>();
                if (particles != null)
                    Destroy(particles.gameObject);
            }


            //*sigh* For fucks sake why isn't there a way to easily enumerate all of the items in the collection?!
            //I won't be able to check things like uid so possibly people could use this to dupe items by swaping them and save/loading
            int lootCount = Loot.Items.GetNumItems();
            var corpseCount = 0;
            foreach (var item in Corpse.Loot.items)
                corpseCount += item.stackCount;


            //if a difference is found, update the corpse loot data with a serialized list from the loot container
            if (lootCount != corpseCount)
                 Corpse.Loot.items = Loot.Items.SerializeItems();

        }

        /// <summary>
        /// Compares equivalency of of two loot containers. Sorta.
        /// </summary>
        /// <param name="col1"></param>
        /// <param name="col2"></param>
        /// <returns></returns>
        public static int CompareItemsList(ItemData_v1[] loot1, ItemData_v1[] loot2)
        {
            //there is no simple way to get all items so we're going to have to get inventive here
            if (loot1.Length > loot2.Length) return 1;
            else if (loot1.Length < loot2.Length) return -1;
            else
            {
                //we are going to be lazy here and only compare uid, shortname, and stack count
                for(int i = 0; i < loot1.Length; i++)
                {
                    var item1 = loot1[i];
                    var item2 = loot2[i];

                    if (item1 == null && item2 != null)
                        return 1;
                    if (item1 != null && item2 == null)
                        return -1;
                    if (item1.uid != item2.uid)
                        return item1.uid.CompareTo(item2.uid);
                    if (item1.stackCount != item2.stackCount)
                        return item1.stackCount.CompareTo(item2.stackCount);
                }

                return 0;
            }
        }


    }
}
