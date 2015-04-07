using System;
using System.Text;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;

//-------------------------------------------------------------
//
//    Fusenet - The Future of Usenet
//              http://github.com/fusenet
//
//    This library is free software; you can redistribute it
//    and modify it under the terms of the GNU General Public
//    License as published by the Free Software Foundation.
//
//-------------------------------------------------------------

namespace Fusenet.Utils
{
    internal class IndexedCollection : IndexedObject 
    {
        private int zID;
        private int zIndex;

        private int IdCounter = 0;
        private Boolean zCompletedAdding = false;
        private ConcurrentDictionary<int, IndexedObject> zCol;

        internal IndexedCollection()
        {
            zCol = new ConcurrentDictionary<int, IndexedObject>();
        }

        internal IndexedCollection(List<IndexedObject> cList)
        {
            CreateCollection(cList.Count);
            if (!Add(cList)) { throw new Exception("Add failed"); }
        }

        internal IndexedCollection(int Capacity)
        {
            CreateCollection(Capacity);
        }

        internal void CreateCollection(int Capacity)
        {
            zCol = new ConcurrentDictionary<int, IndexedObject>(1,Capacity);
        }

        public int ID
        {
            get { return zID; }
            set { zID = value; }
        }

        public int Index
        {
            get { return zIndex; }
            set { zIndex = value; }
        }

        public int CompareTo(object obj) { return CompareTo(obj as IndexedObject); }
        public int CompareTo(IndexedObject obj) { return this.Index.CompareTo(obj.Index); }

        public void Clear() { zCol.Clear(); }
        public int Count { get { return zCol.Count; } }
        public bool IsEmpty { get { return (zCol.IsEmpty); } }
        private Boolean IsAddingCompleted { get { return zCompletedAdding; } }
        public bool IsCompleted { get { return (IsEmpty && IsAddingCompleted); } }
        internal bool ContainsKey(int ID) { return zCol.ContainsKey(ID); }

        public bool Remove(int ID = -1)
        {
            if (ID == -1)
            {
                Clear();
                return true;
            }

            IndexedObject cItem = null;
            while (!zCol.TryRemove(ID, out cItem))
            { if (!zCol.ContainsKey(ID)) { return false; } }

            return true;
        }

        public IndexedObject Take() // Tries to take an object 
        {                           //  with the lowest index

            while (!IsEmpty) 
            {
                IndexedObject cItem = null;

                if (zCol.TryRemove(Next, out cItem))
                {
                    return cItem;
                }
            }

            return null;
        }

        internal IndexedObject Item(int ID)
        {
            IndexedObject cItem = null;

            while (zCol.ContainsKey(ID))
            {
                if (zCol.TryGetValue(ID, out cItem)) { return cItem; }
            }

            return null;
        }

        internal List<int> KeyList(int KeyID = -1)
        {
            if (KeyID == -1) { return Common.EnumInt(zCol.Keys.GetEnumerator()); }
            List<int> sList = new List<int>();
            if (zCol.ContainsKey(KeyID)) { sList.Add(KeyID); }
            return sList;
        }

        internal int GetIndex(int ID)
        {
            IndexedObject cItem = null;

            while (zCol.ContainsKey(ID))
            {
                if (zCol.TryGetValue(ID, out cItem)) { return cItem.Index; }
            }

            return -1;
        }

        internal List<IndexedObject> ObjectList(int ObjectID = -1)
        {
            List<IndexedObject> sList = new List<IndexedObject>();

            if (ObjectID == -1)
            {
                foreach (IndexedObject cItem in Common.EnumObj(zCol.Values.GetEnumerator()))
                {
                    sList.Add(cItem);
                }

                return sList;
            }

            IndexedObject cObj = Item(ObjectID);
            if (cObj != null) { sList.Add(cObj); }
            return sList;
        }

        private int Next
        {
            get
            {
                if (zCol.IsEmpty) { return -1; }
                List<IndexedObject> oList = Common.EnumObj(zCol.Values.GetEnumerator());
                if (oList.Count == 0) { return -1; }

                oList.Sort();
                return oList[0].ID;
            }
        }

        internal bool Add(IndexedObject cObj)
        {
            return Add(Interlocked.Increment(ref IdCounter), cObj); 
        }

        internal bool Add(int ID, IndexedObject cObj)
        {
            if (cObj == null) { return false; }
            if (IsAddingCompleted) { return false; }

            cObj.ID = ID;
            cObj.Index = ID;

            return zCol.TryAdd(ID, cObj);
        }

        internal bool Add(List<IndexedObject> cList)
        {
            if (cList == null) { return false; }
            if (!zCol.IsEmpty) { return false; }

            foreach (IndexedObject cObj in cList)
            {
                if (!Add(cObj)) { return false; }
            }

            zCompletedAdding = true;
            return true;
        }

    } // <nc-C4PTF468>

    internal class SortIntAscending : IComparer<int>
    {
        int IComparer<int>.Compare(int a, int b)
        {
            if (a > b)
                return 1;
            if (a < b)
                return -1;
            else
                return 0; // equal
        }
    }  // <xHtW91l3AGc>

    internal class SortIntDescending : IComparer<int>
    {
        int IComparer<int>.Compare(int a, int b)
        {
            if (a > b)
                return -1; //normally greater than = 1
            if (a < b)
                return 1; // normally smaller than = -1
            else
                return 0; // equal
        }
    } // <qMddkVUwYt4>

} // <P0Z6yRJ1fYo>
