using Bombd.Helpers;
using Bombd.Logging;

namespace Bombd.Types.Network.Arbitration;

public class NetArbitrationServer(Action<ItemNode, ItemAcquirerNode> onReleaseNode)
{
    public readonly List<ItemNode> Items = [];
    
    public bool Create(int type, uint owner, AcquireBehavior behavior, int id)
    {
        Logger.LogDebug<NetArbitrationServer>($"NetItem create 0x{id:x8} ({type:x}), player 0x{owner:x8}");
        
        if (Items.Exists(item => item.Uid == id))
        {
            Logger.LogDebug<NetArbitrationServer>($"NetItem already exists 0x{id:x8} ({type:x}), player 0x{owner:x8}");
            return false;
        }
        
        Items.Add(new ItemNode
        {
            TypeId = type,
            Uid = id,
            OwnerUid = owner,
            AcquireBehavior = behavior
        });

        return true;
    }
    
    public bool Destroy(int id, uint owner)
    {
        Logger.LogDebug<NetArbitrationServer>($"NetItem destroy 0x{id:x8}, player 0x{owner:x8}");
        ItemNode? item = Items.FirstOrDefault(item => item.Uid == id);
        if (item == null)
        {
            Logger.LogDebug<NetArbitrationServer>($"NetItem destroy fail, doesn't exist, 0x{id:x8}, player 0x{owner:x8}");
            return false;
        }

        if (item.OwnerUid != owner)
        {
            Logger.LogDebug<NetArbitrationServer>($"NetItem destroy fail, not owned, 0x{id:x8}, player 0x{owner:x8}");
            return false;
        }
        
        // Do we need to release any acquirers, or will the game naturally destroy them?
        Items.Remove(item);
        return true;
    }
    
    public bool Acquire(int id, uint player, int timeout)
    {
        Logger.LogDebug<NetArbitrationServer>($"NetItem request acquire 0x{id:x8}, player 0x{player:x8}, timeout: {timeout}");
        ItemNode? item = Items.FirstOrDefault(item => item.Uid == id);
        if (item == null)
        {
            Logger.LogDebug<NetArbitrationServer>($"NetItem acquire fail, doesn't exist, 0x{id:x8}, player 0x{player:x8}");
            return false;
        }

        ItemAcquirerNode? node = item.Acquirers.FirstOrDefault(node => node.Uid == player);
        if (node != null)
        {
            // try sending the item confirmation first instead of failing, helps with our answer being too slow to them
            Logger.LogDebug<NetArbitrationServer>($"NetItem already acquired by this player, confirming again, 0x{id:x8}, player 0x{player:x8}");
            return true;
        }

        if (item.Acquirers.Count > 0 && item.AcquireBehavior == AcquireBehavior.SingleAcquire)
        {
            Logger.LogDebug<NetArbitrationServer>($"NetItem acquire fail, acquired by another player, 0x{id:x8}, player 0x{player:x8}");
            return false;
        }
        
        item.Acquirers.Add(new ItemAcquirerNode
        {
            AcquireTime = (uint)TimeHelper.LocalTime,
            Timeout = timeout,
            Uid = player
        });
        
        Logger.LogDebug<NetArbitrationServer>($"NetItem acquire 0x{id:x8}, player 0x{player:x8}");
        return true;
    }

    public void Release(int uid, uint acquirer)
    {
        Logger.LogDebug<NetArbitrationServer>($"NetItem release 0x{uid:x8}, player 0x{acquirer:x8}");
        
        ItemNode? item = Items.FirstOrDefault(item => item.Uid == uid);
        if (item == null)
        {
            Logger.LogDebug<NetArbitrationServer>($"NetItem release fail, doesn't exist, 0x{uid:x8}, player 0x{acquirer:x8}");
            return;
        }
        
        ItemAcquirerNode? node = item.Acquirers.FirstOrDefault(node => node.Uid == acquirer);
        if (node == null)
        {
            Logger.LogDebug<NetArbitrationServer>($"NetItem release fail, not acquired, 0x{uid:x8}, player 0x{acquirer:x8}");
            return;
        }
        
        item.Acquirers.Remove(node);
        onReleaseNode(item, node);
    }
    
    public void Update()
    {
        uint time = (uint)TimeHelper.LocalTime;
        foreach (ItemNode item in Items)
        {
            List<ItemAcquirerNode> expired = 
                item.Acquirers.Where(acquirer => time >= acquirer.AcquireTime + acquirer.Timeout && acquirer.Timeout != -1).ToList();
            
            if (expired.Count == 0) continue;
            
            var removalSet = new HashSet<ItemAcquirerNode>(expired);
            item.Acquirers.RemoveAll(x => removalSet.Contains(x));

            foreach (ItemAcquirerNode acquirer in expired)
            {
                Logger.LogDebug<NetArbitrationServer>($"NetItem release expired item 0x{item.Uid:x8}, player 0x{acquirer.Uid:x8}");
                onReleaseNode(item, acquirer);
            }
        }
    }
}