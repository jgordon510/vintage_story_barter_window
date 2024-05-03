using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static System.Net.Mime.MediaTypeNames;

namespace barter_window
{
    public class BarterWindowGui : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "barterwindow";
        private static IInventory myInv;
        private static InventoryGeneric theirInv;
        private string myId;
        private string theirId;
        public BarterWindowGui(ICoreClientAPI capi, string otherId) : base(capi)
        {
            SetupDialog();
            myId = capi.World.Player.PlayerUID;
            theirId = otherId;
            capi.Network.GetChannel("barter_change")
                .SetMessageHandler<BarterChange>(OnClientReceiveChange);
        }
        private void SetupDialog()
        {
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding, 20.0);
            ElementBounds insetBounds = ElementBounds.Fixed(10, 30, 220, 300);
            ElementBounds slotGridBounds1 = ElementBounds.Fixed(10, 40, 200, 100);
            ElementBounds textBounds1 = ElementBounds.Fixed(0, 10, 200, 20);
            ElementBounds slotGridBounds2 = ElementBounds.Fixed(10, 190, 200, 100);
            ElementBounds textBounds2 = ElementBounds.Fixed(0, 160, 210, 20);
            ElementBounds buttonBounds = ElementBounds.Fixed(70, 350, 100, 40);
            ElementBounds buttonTextBounds = ElementBounds.Fixed(90, 360, 100, 40);

            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.fixedHeight -= 20;
            bgBounds.WithChildren(insetBounds);
            bgBounds.WithChildren(buttonBounds);
            bgBounds.WithChildren(buttonTextBounds);
            insetBounds.WithChildren(slotGridBounds1);
            insetBounds.WithChildren(slotGridBounds2);
            insetBounds.WithChildren(textBounds1);
            insetBounds.WithChildren(textBounds2);
            if(theirInv==null) {
                capi.Logger.Debug("NEW THEIR INVENTORY");
                theirInv = new(8, null, null, capi);
            } else
            {
                capi.Logger.Debug("EXISTING THEIR INVENTORY FOUND");
            }
            if(myInv == null) {
                capi.Logger.Debug("no my inventory found");
                //create the barter inventory on the first occurence of the gui
                //we'll need to forcing it empty when the gui closes or the user logs
                myInv = new InventoryGeneric(8, null, "barter", capi);
                capi.World.Player.InventoryManager.OpenInventory(myInv);
            }
            
            SingleComposer = capi.Gui.CreateCompo("barterwindow", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Barter Window", OnTitleBarCloseClicked)
                .AddInset(insetBounds, 4, 0.85f)
                .AddDynamicText("_their stuff_", CairoFont.WhiteSmallishText(), textBounds1, "theirstuff")
                .AddItemSlotGrid(theirInv, DoNotSendPacket, 4, slotGridBounds1, "slots1") //callback goes nowhere
                .AddDynamicText("_my stuff_", CairoFont.WhiteSmallishText(), textBounds2, "mystuff")
                .AddItemSlotGrid(myInv, DoSendPacket, 4, slotGridBounds2, "slots2")
                .AddSmallButton(Lang.Get("       "), OnOfferClick, buttonBounds, EnumButtonStyle.Normal, "offerbutton")
                .AddDynamicText("Offer!", CairoFont.WhiteSmallishText(), buttonTextBounds, "buttontext")
                .Compose()
           ;
           theirInv.PutLocked = true;
           theirInv.TakeLocked = true;
        }
        private bool OnOfferClick()
        {
            GuiElementDynamicText textElem = SingleComposer.GetDynamicText("buttontext");
            textElem.Text = "Retract!";
            textElem.Bounds.WithFixedOffset(-10, 0);
            capi.Logger.Debug(textElem.Text);
            SingleComposer.ReCompose();
            //this leads to the escrow process offer->retract (back to offer)->accept
            return true;
        }
        private void OnTitleBarCloseClicked()
        {
            TryClose();
            //deal with dumping contents of barter window
        }
        private void DoSendPacket(object p)
        {
            //this syncs my actual inventory
            myInv.Open(capi.World.Player);
            capi.Network.SendPacketClient(p);
            //this reports it to the other player
            capi.Network.GetChannel("barter_change").SendPacket(new BarterChange()
            {
                senderId = myId,
                receiverId = theirId,
                changes = null //this is derived on the server!
            });
        }

        private void DoNotSendPacket(object p)
        {
            //We don't need to sync back the dummy grid.  This should never fire with put/takeLocked
        }

        private void OnClientReceiveChange(BarterChange change)
        {
            //temporarilly remove the locks, because without that we can't alter the inventory
            theirInv.PutLocked = false;
            theirInv.TakeLocked = false;
            
            for(int i = 0; i < 8 ; i++)
            {
                //remember: theirInv is a dummy - unsynced back to server.
                //the strings are serialized on the server so they shouldn't be spoofed
                ItemSlot theirSlot = theirInv[i];
                //[0] = index; [1] = codestring; [2] = quantity as string
                string[] changeSlot = change.changes[i].Split(";");
                capi.Logger.Debug(change.changes[i]);
                if (changeSlot[1] == "") {
                    capi.Logger.Debug("EMPTY SLOT");
                    //remove anything here - like gamelan
                    int quant = 1;
                    if (theirSlot?.Itemstack?.StackSize != null) quant = theirSlot.Itemstack.StackSize;
                    theirSlot.TakeOut(quant);
                    continue; 
                }
                capi.Logger.Debug("found: {0}" , changeSlot[1]);
                Block newBlock = capi.World.GetBlock(new AssetLocation(changeSlot[1]));
                theirSlot.Itemstack = new ItemStack(newBlock);
                theirSlot.Itemstack.StackSize = Int32.Parse(changeSlot[2]);
                theirSlot.MarkDirty(); //I think this does nothing here
            }
            //lock it back up
            theirInv.PutLocked = true;
            theirInv.TakeLocked = true;
        }
    }
}
