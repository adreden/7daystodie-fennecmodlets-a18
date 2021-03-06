﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/**
 *  Tile entity class for block transformer. Takes input items and changes them into output items. 
 */
public class TileEntityBlockTransformer : TileEntityLootContainer
{
    /**
     * Loads the tile entity data when instantiated from placing a block.
     */

    public TileEntityBlockTransformer(Chunk _chunk) : base(_chunk)
    {
        this.tQueue  = new TransformationQueue();
        this.useHash = true;
        Log.Out("Tile Entity created.");
        foreach (KeyValuePair<string, TransformationData> entry in TransformationData.hashmap)
        {
            Log.Out("Hash: " + entry.Key);
            Log.Out("Data: " + entry.Value.ToString());
        }
        Log.Out("All hashes read.");
    }


    /**
     * Loads the tile entity by copying another of its kind.
     */

    private TileEntityBlockTransformer(TileEntityBlockTransformer _other) : base(null)
    {
        this.lootListIndex      = _other.lootListIndex;
        this.containerSize      = _other.containerSize;
        this.items              = ItemStack.Clone(_other.items);
        this.bTouched           = _other.bTouched;
        this.worldTimeTouched   = _other.worldTimeTouched;
        this.bPlayerBackpack    = _other.bPlayerBackpack;
        this.bPlayerStorage     = _other.bPlayerStorage;
        this.bUserAccessing     = _other.bUserAccessing;
        this.collection         = _other.collection;
        this.tQueue             = _other.tQueue;
        this.useHash            = _other.useHash;
        this.requiresPower      = _other.requiresPower;
        this.requiresHeat       = _other.requiresHeat;
        this.powerSources       = _other.powerSources;
        this.heatSources        = _other.heatSources;
    }


    /**
     * Sets the above method public
     */

    public new TileEntityBlockTransformer Clone()
    {
        return new TileEntityBlockTransformer(this);
    }


    /**
     * Copies properties from another tile entity of this type into this one.
     */

    public void CopyLootContainerDataFromOther(TileEntityBlockTransformer _other)
    {
        this.lootListIndex      = _other.lootListIndex;
        this.containerSize      = _other.containerSize;
        this.items              = ItemStack.Clone(_other.items);
        this.bTouched           = _other.bTouched;
        this.worldTimeTouched   = _other.worldTimeTouched;
        this.bPlayerBackpack    = _other.bPlayerBackpack;
        this.bPlayerStorage     = _other.bPlayerStorage;
        this.bUserAccessing     = _other.bUserAccessing;
        this.collection         = _other.collection;
        this.tQueue             = _other.tQueue;
        this.useHash            = _other.useHash;
        this.requiresPower      = _other.requiresPower;
        this.requiresHeat       = _other.requiresHeat;
        this.powerSources       = _other.powerSources;
        this.heatSources        = _other.heatSources;
    }


    /**
     * Each game tick, this method is called.
     */

    public override void UpdateTick(World world)
    {
        if (!this.UpdateCanHappen(world))
        {
            return;
        }

        this.HandleAddingToQueue();
        this.HandleProcessingQueue();
    }


    /**
     * Checks that an update can happen.
     */

    private bool UpdateCanHappen(World world)
    {
        return (this.IsPowered() & this.IsHeated() & (this.tQueue.QueueDefinedAndNotEmpty() | !this.IsEmpty()));
    }


    /**
     * Checks if block is powered. It is required to be next to an electric wire relay.
     */

    private bool IsPowered()
    {
        if (!this.requiresPower)
        {
            return true;
        }

        World world = GameManager.Instance.World;
        Vector3i tileEntityPos = this.ToWorldPos();
        List<Vector3i> nearby = CoordinateHelper.GetCoOrdinatesAround(tileEntityPos, true, 1, 1, 1);

        if (nearby.Count == 0)
        {
            return false;
        }

        Dictionary<Vector3i, TileEntity> tileEntitiesNearby = CoordinateHelper.GetTileEntitiesInCoordinates(world, nearby, TileEntityType.Powered);

        if (tileEntitiesNearby.Count == 0)
        {
            return false;
        }

        foreach (KeyValuePair<Vector3i, TileEntity> entry in tileEntitiesNearby)
        {
            TileEntityPowered otherTileEntity = entry.Value as TileEntityPowered;
            Vector3i otherTileEntityPos = entry.Key;

            foreach (string powerSource in this.powerSources)
            {
                string name = CoordinateHelper.GetBlockNameAtCoordinate(world, otherTileEntityPos);
                if (!CoordinateHelper.BlockAtCoordinateIs(world, otherTileEntityPos, powerSource))
                {
                    continue;
                }

                if (otherTileEntity.IsPowered)
                {
                    return true;
                }
            }
        }
        return false;
    }


    /**
     * Returns true if a heat source is under the block
     */

    private bool IsHeated()
    {
        if (!this.requiresHeat)
        {
            return true;
        }

        World world = GameManager.Instance.World;
        Vector3i tileEntityPos = this.ToWorldPos();
        Vector3i posUnderTE = CoordinateHelper.GetCoordinateBelow(tileEntityPos);

        // If TE is on very bottom of world this will prevent out of boundary shenanigans.
        if (tileEntityPos == posUnderTE)
        {
            return false;
        }

        Dictionary<Vector3i, TileEntity> tileEntitiesUnderneath = CoordinateHelper.GetTileEntitiesInCoordinates(world, new List<Vector3i>() { posUnderTE }, TileEntityType.Workstation);

        if (tileEntitiesUnderneath.Count == 0)
        {
            return false;
        }

        foreach (KeyValuePair<Vector3i, TileEntity> entry in tileEntitiesUnderneath)
        {
            TileEntityWorkstation otherTileEntity = entry.Value as TileEntityWorkstation;
            Vector3i otherTileEntityPos = entry.Key;

            foreach (string heatSource in this.heatSources)
            {
                if (!CoordinateHelper.BlockAtCoordinateIs(world, otherTileEntityPos, heatSource))
                {
                    continue;
                }

                // Checks that there is fuel being used for the workstation as well as if it's burning.
                foreach (ItemStack itemStack in otherTileEntity.Fuel)
                {
                    if (itemStack != null & !itemStack.Equals(ItemStack.Empty.Clone()))
                    {
                        if (otherTileEntity.IsActive(world))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }


    /**
     * Deals with inserting items into the queue and removing them from the block inventory.
     */

    public void HandleAddingToQueue()
    {
        foreach (TransformationData tData in this.collection.collection)
        {
            ulong transformationTime;
            List<Tuple<int, ItemStack>> locations;
            TransformationJob job;
            if (!this.QueueReadyItems(tData, out transformationTime, out locations, out job))
            {
                continue;
            }

            if (job == null)
            {
                continue;
            }

            ItemStack[] previousState;
            if (!this.StoreReadyItems(locations, out previousState))
            {
                this.RestorePreviousState(previousState);
                this.tQueue.RemoveEntry(job);
            }
            this.tileEntityChanged();
        }
    }


    /**
     * Retrieves jobs from the queue and processes them.
     */

    private void HandleProcessingQueue()
    {
        if (!this.tQueue.QueueDefinedAndNotEmpty())
        {
            return;
        }

        List<TransformationJob> jobs = this.tQueue.GetNextTransformations();
        foreach (TransformationJob job in jobs)
        {
            ItemStack[] previousState;
            bool processed = this.ProcessQueueJob(job, out previousState);
            if (processed)
            {
                this.tQueue.RemoveEntry(job);
                continue;
            }
            job.MarkJobNotInProgress();
            this.RestorePreviousState(previousState);
        }
    }


    /**
     * Queues ready transformations.
     */

    private bool QueueReadyItems(TransformationData tData, out ulong transformationTime, out List<Tuple<int, ItemStack>>stackLocations, out TransformationJob job)
    {
        transformationTime = 0;
        job = null;
        if (!tData.HasAllInputs(InventoryHelper.GetItemStacksFromFilledSlots(this.items), out stackLocations))
        {
            return false;
        }
        return this.AddToQueue(tData, out transformationTime, out job);
    }


    /**
     * Removes each item from the inventory and then returns true if all were successfully removed.
     * Outputs the original state of the container in case of failure for safe reverse.
     */

    private bool StoreReadyItems(List<Tuple<int, ItemStack>> locations, out ItemStack[] previousState)
    {
        previousState   = (ItemStack[])this.items.Clone();
        foreach (Tuple<int, ItemStack> entry in locations)
        {
            if (!InventoryHelper.RemoveItemsInSlot(this.items, entry.Item1, entry.Item2.count))
            {
                return false;
            }
        }
        return true;
    }
   

    /**
     * Adds to the queue, giving out the transformation time and the job added.
     */

    private bool AddToQueue(TransformationData tData, out ulong time, out TransformationJob job)
    {
        time = 0;
        job = null;
        bool added = this.tQueue.Add(tData, out time, out job);
        return added;
    }


    /**
     * Processes a queue job.
     */

    private bool ProcessQueueJob(TransformationJob job, out ItemStack[] previousState)
    {
        previousState = (ItemStack[])this.items.Clone();
        List<ItemStack> outputs = job.GetTransformationData().GetAllOutputsAfterProbabilityCalculation();
        List<int> assignedSlots = new List<int>();
        List<Tuple<int, ItemStack>> stacksToAdd = new List<Tuple<int, ItemStack>>();
        bool canAddAll = true;

        // If there are no ouputs after probability calculation, then congrats, nothing to do, success.
        if (outputs.Count == 0)
        {
            return true;
        }

        foreach (ItemStack output in outputs)
        {
            List<Tuple<int, ItemStack>> whereToAdd;
            if (!InventoryHelper.RoomInSlotsFor(this.items, output, ref assignedSlots, out whereToAdd))
            {
                canAddAll = false;
                return false;
            }

            stacksToAdd.AddRange(whereToAdd);
        }
        
        if (canAddAll)
        {
            foreach (Tuple<int, ItemStack> positionAndStack in stacksToAdd)
            {
                if (!InventoryHelper.TryAddItemToSlot(this.items, positionAndStack.Item1, positionAndStack.Item2))
                {
                    return false;
                }
            }
        }

        return canAddAll;
    }


    /**
     * Restores the previous state of the items before removal in case of failure.
     */

    private void RestorePreviousState(ItemStack[] previousState)
    {
        this.items = ItemStack.Clone(previousState, 0, this.items.Length);
        this.tileEntityChanged();
    }
    

    /**
     * Reads data into the tile entity when loaded.
     */

    public override void read(BinaryReader _br, StreamModeRead _eStreamMode)
    {
        base.read(_br, _eStreamMode);
        string collectionString = _br.ReadString();
        this.collection = TransformationCollection.Read(collectionString, useHash);
        string tQueueString = _br.ReadString();
        this.tQueue = tQueue.Read(tQueueString, useHash);
        this.requiresPower = _br.ReadBoolean();
        this.powerSources = StringHelpers.WriteStringToList(_br.ReadString());
        this.requiresHeat = _br.ReadBoolean();
        this.heatSources = StringHelpers.WriteStringToList(_br.ReadString());
    }


    /**
     * Saves tile entity data to the strem.
     */

    public override void write(BinaryWriter _bw, StreamModeWrite _eStreamMode)
    {
        base.write(_bw, _eStreamMode);
        _bw.Write(this.collection.Write(useHash));
        _bw.Write(this.tQueue.Write(useHash));
        _bw.Write(this.requiresPower);
        _bw.Write(StringHelpers.WriteListToString(this.powerSources));
        _bw.Write(this.requiresHeat);
        _bw.Write(StringHelpers.WriteListToString(this.heatSources));
        
    }


    /**
     * What happens when the block is upgraded or downgraded.
     */

    public void UpgradeDowngradeFrom(TileEntityBlockTransformer _other)
    {
        base.UpgradeDowngradeFrom(_other);
        this.OnDestroy();
        if (_other is TileEntityBlockTransformer)
        {
            TileEntityBlockTransformer tileEntityBlockTransformer = _other as TileEntityBlockTransformer;
            this.bTouched = tileEntityBlockTransformer.bTouched;
            this.worldTimeTouched = tileEntityBlockTransformer.worldTimeTouched;
            this.bPlayerBackpack = tileEntityBlockTransformer.bPlayerBackpack;
            this.bPlayerStorage = tileEntityBlockTransformer.bPlayerStorage;
            this.items = ItemStack.Clone(tileEntityBlockTransformer.itemsArr, 0, this.containerSize.x * this.containerSize.y);
            if (this.items.Length != this.containerSize.x * this.containerSize.y)
            {
                Log.Error("Error upgrading.");
            }
        }
    }
    

    /**
     * What happens when the tile entities change state.
     */

    private void tileEntityChanged()
    {
        for (int i = 0; i < this.listeners.Count; i++)
        {
            this.listeners[i].OnTileEntityChanged(this, 0);
        }
    }


    /**
     * Returns the tile entity enum type for comparison.
     */

    public override TileEntityType GetTileEntityType()
    {
        return TileEntityType.BlockTransformer;
    }


    /**
     * Sets the collection variable to hold details of what items transform into what other ones.
     */

    public void SetTransformationCollection(TransformationCollection collection)
    {
        this.collection = collection;
    }


    /**
     * Sets required power.
     */

    public void SetRequirePower(bool requirePower, List<string> powerSourceBlocks)
    {
        this.requiresPower = requirePower;
        this.powerSources = powerSourceBlocks;
    }


    /**
     * Sets required heat.
     */
    
    public void SetRequireHeat(bool requireHeat, List<string> heatSourceBlocks)
    {
        this.requiresHeat = requireHeat;
        this.heatSources = heatSourceBlocks;
    }

    private bool useHash;
    private bool bDisableModifiedCheck;
    private bool requiresPower;
    private bool requiresHeat;
    private List<string> powerSources;
    private List<string> heatSources;
    private Vector2i containerSize;
    private ItemStack[] itemsArr;
    private List<Entity> entList;
    private TransformationCollection collection;
    private TransformationQueue tQueue;
}
