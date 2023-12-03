﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using NLog;

namespace AAEmu.Login.Utils;

public class IdManager
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private BitSet _freeIds;
    private int _freeIdCount;
    private int _nextFreeId;

    private readonly string _name;
    private readonly uint _firstId = 0x00000001;
    private readonly uint _lastId = 0xFFFFFFFF;
    private readonly uint[] _exclude;
    private readonly int _freeIdSize;
    private readonly string[,] _objTables;
    private readonly bool _distinct;
    private readonly object _lock = new();

    public IdManager(string name, uint firstId, uint lastId, string[,] objTables, uint[] exclude, bool distinct = false)
    {
        _name = name;
        _firstId = firstId;
        _lastId = lastId;
        _objTables = objTables;
        _exclude = exclude;
        _distinct = distinct;
        _freeIdSize = (int)(_lastId - _firstId);
        PrimeFinder.Init();
    }

    public bool Initialize()
    {
        try
        {
            _freeIds = new BitSet(PrimeFinder.NextPrime(100000));
            _freeIds.Clear();
            _freeIdCount = _freeIdSize;

            foreach (var usedObjectId in ExtractUsedObjectIdTable())
            {
                if (_exclude.Contains(usedObjectId))
                    continue;
                var objectId = (int)(usedObjectId - _firstId);
                if (usedObjectId < _firstId)
                {
                    Logger.Warn("{0}: Object ID {1} in DB is less than {2}", _name, usedObjectId, _firstId);
                    continue;
                }

                if (objectId >= _freeIds.Count)
                    IncreaseBitSetCapacity(objectId + 1);
                _freeIds.Set(objectId);
                Interlocked.Decrement(ref _freeIdCount);
            }

            _nextFreeId = _freeIds.NextClear(0);
            Logger.Info("{0} successfully initialized", _name);
        }
        catch (Exception e)
        {
            Logger.Error("{0} could not be initialized correctly", _name);
            Logger.Error(e);
            return false;
        }

        return true;
    }
    private uint[] ExtractUsedObjectIdTable()
    {
        if (_objTables.Length < 2)
            return Array.Empty<uint>();

        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                var query = "SELECT " + (_distinct ? "DISTINCT " : "") + _objTables[0, 1] + ", 0 AS i FROM " +
                            _objTables[0, 0];
                for (var i = 1; i < _objTables.Length / 2; i++)
                    query += " UNION SELECT " + (_distinct ? "DISTINCT " : "") + _objTables[i, 1] + ", " + i +
                             " FROM " + _objTables[i, 0];

                command.CommandText = "SELECT COUNT(*), COUNT(DISTINCT " + _objTables[0, 1] + ") FROM ( " + query +
                                      " ) AS all_ids";
                command.Prepare();
                int count;
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        throw new GameException("IdManager: can't extract count ids");
                    if (reader.GetInt32(0) != reader.GetInt32(1) && !_distinct)
                        throw new GameException("IdManager: there are duplicates in object ids");
                    count = reader.GetInt32(0);
                }

                if (count == 0)
                    return Array.Empty<uint>();

                var result = new uint[count];
                Logger.Info("{0}: Extracting {1} used id's from data tables...", _name, count);

                command.CommandText = query;
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    var idx = 0;
                    while (reader.Read())
                    {
                        result[idx] = reader.GetUInt32(0);
                        idx++;
                    }

                    Logger.Info("{0}: Successfully extracted {1} used id's from data tables.", _name, idx);
                }

                return result;
            }
        }
    }

    public virtual void ReleaseId(uint usedObjectId)
    {
        var objectId = (int)(usedObjectId - _firstId);
        if (objectId > -1)
        {
            _freeIds.Clear(objectId);
            if (_nextFreeId > objectId)
                _nextFreeId = objectId;
            Interlocked.Increment(ref _freeIdCount);
        }
        else
            Logger.Warn("{0}: release objectId {1} failed", _name, usedObjectId);
    }

    public virtual void ReleaseId(IEnumerable<uint> usedObjectIds)
    {
        foreach (var id in usedObjectIds)
            ReleaseId(id);
    }

    public uint GetNextId()
    {
        lock (_lock)
        {
            var newId = _nextFreeId;
            _freeIds.Set(newId);
            Interlocked.Decrement(ref _freeIdCount);

            var nextFree = _freeIds.NextClear(newId);

            while (nextFree < 0)
            {
                nextFree = _freeIds.NextClear(0);
                if (nextFree < 0)
                {
                    if (_freeIds.Count < _freeIdSize)
                        IncreaseBitSetCapacity();
                    else
                        throw new GameException("Ran out of valid Id's.");
                }
            }

            _nextFreeId = nextFree;
            return (uint)newId + _firstId;
        }
    }

    public uint[] GetNextId(int count)
    {
        var res = new uint[count];
        for (var i = 0; i < count; i++)
            res[i] = GetNextId();
        return res;
    }

    private void IncreaseBitSetCapacity()
    {
        var size = PrimeFinder.NextPrime(_freeIds.Count + _freeIdSize / 10);
        if (size > _freeIdSize)
            size = _freeIdSize;
        var newBitSet = new BitSet(size);
        newBitSet.Or(_freeIds);
        _freeIds = newBitSet;
    }

    private void IncreaseBitSetCapacity(int count)
    {
        var size = PrimeFinder.NextPrime(count);
        if (size > _freeIdSize)
            size = _freeIdSize;
        var newBitSet = new BitSet(size);
        newBitSet.Or(_freeIds);
        _freeIds = newBitSet;
    }
}
