using DaggerfallWorkshop.Game;
using UnityEngine;

namespace RIP
{
    /// <summary>
    /// A dummy object that is spawned whenever the player enters a tavern. It keeps track of changes to the player's
    /// rented rooms list every frame to determine if a new respawn location should be saved.
    /// </summary>
    public class RipTavernPoller : MonoBehaviour
    {
        int LastRoomCount = 0;

        void Start()
        {
            RipDeathHandler.Log("Started polling rentals...");
            LastRoomCount = GameManager.Instance.PlayerEntity.RentedRooms.Count;
        }

        private void OnDestroy()
        {
            RipDeathHandler.Log("...stopped polling rentals.");
        }

        void Update()
        {
            var player = GameManager.Instance.PlayerEntity;

            if(Input.GetKeyUp("L"))
            {
                var worldPos = GameManager.Instance.PlayerGPS.CurrentMapPixel;
                RipDeathHandler.RipRespawnData data = new RipDeathHandler.RipRespawnData()
                {
                    AnchorPos = RipDeathHandler.GetPlayerLocationForAnchor(),
                    WorldPosX = worldPos.X,
                    WorldPosY = worldPos.Y,
                    RespawnTypeId = RipDeathHandler.RespawnType.RentedRoom,

                };

                //todo: serialize the fuck out of this!
                
            }

            //check for expired rooms. it's *possible* they might rent a room this exact frame,
            //which would cause us to miss the room count change but I don't think this unlikely
            //event is worth the cost of checking every individual room's data evcery frame
            if(player.RentedRooms.Count < LastRoomCount)
            { 
                LastRoomCount = GameManager.Instance.PlayerEntity.RentedRooms.Count;
                return;
            }

            if(player.RentedRooms.Count > LastRoomCount)
            {
                //we aren't getting too fancy here, even if the count has lowered due
                //to a room expiring we'll still take the last one
                LastRoomCount = player.RentedRooms.Count;
                if (LastRoomCount > 0)
                {
                    RipDeathHandler.KeepLocationAsRespawnPoint(RipDeathHandler.RespawnType.RentedRoom);
                    RipDeathHandler.Log("Rental detected!");
                    return;
                }
            }

        }
    }
}
