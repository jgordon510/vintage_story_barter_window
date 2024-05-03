using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using ProtoBuf;
using System.Collections.Generic;
using Vintagestory.API.Util;
using Vintagestory.API.MathTools;
using System.Reflection.Emit;

namespace barter_window
{
    
    public class barter_windowModSystem : ModSystem
    {
        private ICoreServerAPI sapi;
        ICoreClientAPI capi;
        GuiDialog dialog;
        private Dictionary<string, InventoryBase> inventories;
        public override void Start(ICoreAPI api)
        {
            api.Network.RegisterChannel("open_barter_request")
                .RegisterMessageType(typeof(OpenRequest))
                .RegisterMessageType(typeof(OpenResponse))
            ;
            api.Network.RegisterChannel("barter_change")
                .RegisterMessageType(typeof(BarterChange))
            ;
            inventories = new();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;
            capi.Input.RegisterHotKey("barterwindow", "A barter window.", GlKeys.B, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("barterwindow", ToggleGui);
            capi.Network.GetChannel("open_barter_request")
                .SetMessageHandler<OpenResponse>(OnReceiveOpenResponse);
        }
        
        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Logger.Debug("SERVER START LEADERSTATS");
            sapi = api;
            api.Network.GetChannel("open_barter_request")
                .SetMessageHandler<OpenRequest>(OnClientRequestOpen);
            api.Network.GetChannel("barter_change")
                .SetMessageHandler<BarterChange>(OnClientAnnounceChange);
            sapi.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        }

        public void OnPlayerNowPlaying(IServerPlayer byPlayer) {
            InventoryBase inv = new InventoryGeneric(8, null, "barter", sapi);
            byPlayer.InventoryManager.OpenInventory(inv);
            //empty anything that got saved; todo move their barter stuff back when the gui gets closed or they log off
            foreach (ItemSlot slot in inv) 
            {
                int quant = 1;
                if (slot?.Itemstack?.StackSize != null) quant = slot.Itemstack.StackSize;
                slot.TakeOut(quant);
            }
            inventories.Add(byPlayer.PlayerUID, inv);
        }

        private bool ToggleGui(KeyCombination comb)
        {
            if (dialog != null && dialog.IsOpened()) dialog.TryClose();
            else
            {
                capi.Network.GetChannel("open_barter_request").SendPacket(new OpenRequest()
                {
                    uid = capi.World.Player.PlayerUID
                });
            }
            return true;
        }
        
        private void OnClientRequestOpen(IPlayer fromPlayer, OpenRequest request)
        {
            sapi.Logger.Debug("OPENING INVENTORY");

            sapi.Network.GetChannel("open_barter_request").SendPacket(new OpenResponse()
            {
                invId = "barter"
            }, fromPlayer as IServerPlayer);
        }
        
        private void OnReceiveOpenResponse(OpenResponse networkMessage)
        {
            //we need to verify that we're looking at another player to open the dialog
            capi.Logger.Debug("client received {0}", networkMessage.invId);
            if (capi.World?.Player?.CurrentEntitySelection?.Entity == null) return;
            capi.Logger.Debug("NAME {0}",capi.World.Player.CurrentEntitySelection.Entity.GetName());
            if (capi.World.Player.CurrentEntitySelection.Entity is not EntityPlayer) return;
            EntityPlayer player = capi.World.Player.CurrentEntitySelection.Entity as EntityPlayer;
            //spoofing this would allow you to peek inside other player's barter inventories
            //seems like non-issue
            dialog = new BarterWindowGui(capi, player.PlayerUID); 
            dialog.TryOpen();
            // todo we also need to broadcast a message to the other player to open their window
        }


        private void OnClientAnnounceChange(IPlayer fromPlayer, BarterChange change)
        {
            sapi.Logger.Debug("received change");
            InventoryBase inv = inventories.Get(fromPlayer.PlayerUID);
            if (inv == null) {
                sapi.Logger.Debug("THERE WAS NO INVENTORY FOUND WTF");
                return; 
            }
            else sapi.Logger.Debug("HOORARY!");
            //we need to serialize the inventory
            string[] changeList = new string[8];
            int i = 0;
            foreach (ItemSlot item in inv)
            {
                if (item.Empty)
                {
                    changeList[i] = ";;;";
                    i++;
                    continue;
                } 
                string c;
                //still need todo tools
                if (item?.Itemstack?.Item != null)
                {
                    c = item.Itemstack.Item.Code.ToString();
                }
                else
                {
                    c = item.Itemstack.Block.Code.ToString();
                }

                sapi.Logger.Debug("FOUND ITEM: {0}", c);
                string ss = item.Itemstack.StackSize.ToString();

                changeList[i] = i.ToString() + ";" + c + ";" + ss;
                sapi.Logger.Debug(changeList[i]);
                i++;
            }
            IPlayer receiver = sapi.World.PlayerByUid(change.receiverId);
            sapi.Network.GetChannel("barter_change").SendPacket(new BarterChange()
            {
                senderId = change.senderId,
                receiverId = change.receiverId,
                changes = changeList, //this is the real deal; generated server side
            }, receiver as IServerPlayer);
        }
    }
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OpenRequest
    {
        public string uid;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OpenResponse
    {
        public string invId;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class BarterChange
    {
        public string senderId;
        public string receiverId;
        public string[] changes; //this is only sent on server calls; client sends null
    }
}
