#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;

namespace TickZoom.Api
{
	public interface Iterable<T> {
		int Count {
			get;
		}
		ActiveListNode<T> First {
			get;
		}
	}

	/// <summary>
	/// 
	///			var next = list.First;
	///			for( var node = next; node != null; node = next) {
	///				next = node.Next;
	///				var other = node.Value;
	/// 
	/// </summary>
	public class ActiveList<T> : Iterable<T> {
        internal ActiveListNode<T> head;
        internal ActiveListNode<T> tail;
        internal int count;
	    private long version;
        private static int nextListId = 0;
	    private int id;

        // Methods
        public ActiveList()
        {
            id = ++nextListId;
        }

        public ActiveListNode<T> AddAfter(ActiveListNode<T> node, T value)
        {
            this.AssertNode(node);
            var newNode = new ActiveListNode<T>(node.list, value);
            this.InsertNodeBefore(node.next, newNode);
            return newNode;
        }

        public void AddAfter(ActiveListNode<T> node, ActiveListNode<T> newNode)
        {
            this.AssertNode(node);
            this.AssertNewNode(newNode);
            this.InsertNodeBefore(node.next, newNode);
        }

        public void AddBefore(ActiveListNode<T> node, ActiveListNode<T> newNode)
        {
            this.AssertNode(node);
            this.AssertNewNode(newNode);
            this.InsertNodeBefore(node, newNode);
            if (node == this.head)
            {
                this.head = newNode;
            }
        }

        public ActiveListNode<T> AddBefore(ActiveListNode<T> node, T value)
        {
            this.AssertNode(node);
            var newNode = new ActiveListNode<T>(node.list, value);
            this.InsertNodeBefore(node, newNode);
            if (node == this.head)
            {
                this.head = newNode;
            }
            return newNode;
        }

        public void AddFirst(ActiveListNode<T> node)
        {
            this.AssertNewNode(node);
            if (this.head == null)
            {
                this.InsertNodeToEmptyList(node);
            }
            else
            {
                this.InsertNodeBefore(this.head, node);
                this.head = node;
            }
        }

        public ActiveListNode<T> AddFirst(T value)
        {
            var newNode = new ActiveListNode<T>((ActiveList<T>)this, value);
            if (this.head == null)
            {
                this.InsertNodeToEmptyList(newNode);
            } else
            {
                this.InsertNodeBefore(this.head, newNode);
                this.head = newNode;
            }
            return newNode;
        }

        public ActiveListNode<T> SortFirst(T value, Func<T, T, long> comparator)
        {
            var newNode = new ActiveListNode<T>((ActiveList<T>) this, value);
            return SortFirst(newNode, comparator);
        }

	    public ActiveListNode<T> SortFirst(ActiveListNode<T> newNode, Func<T, T, long> comparator)
        {
            if (this.head == null)
            {
                this.InsertNodeToEmptyList(newNode);
                return newNode;
            }

            var node = this.head;
            do
            {
                if (comparator(node.Value, newNode.Value) > 0)
                {
                    this.InsertNodeBefore(node, newNode);
                    if (node == this.head)
                    {
                        this.head = newNode;
                    }
                    return newNode;
                }
                node = node.Next;
            } while (node != null);
            this.InsertNodeAfter(this.tail, newNode);
	        this.tail = newNode;
            return newNode;
        }

        public void ResortFirst(ActiveListNode<T> newNode, Func<T, T, int> comparator)
        {
            if( newNode == null)
            {
                throw new ArgumentNullException("newNode");
            }
            if (newNode.list == this)
            {
                this.RemoveNode(newNode);
            }
            if( newNode.list != null)
            {
                throw new InvalidOperationException("newNode belongs to a different list");
            }
            if (this.head == null)
            {
                this.InsertNodeToEmptyList(newNode);
                return;
            }

            var node = this.head;
            do
            {
                if (comparator(node.Value, newNode.Value) > 0)
                {
                    this.InsertNodeBefore(node, newNode);
                    if (node == this.head)
                    {
                        this.head = newNode;
                    }
                    return;
                }
                node = node.Next;
            } while (node != null);
            this.InsertNodeAfter(this.tail, newNode);
            this.tail = newNode;
            return;
        }

        public ActiveListNode<T> AddLast(T value)
        {
            var newNode = new ActiveListNode<T>((ActiveList<T>)this, value);
            if (this.head == null)
            {
                this.InsertNodeToEmptyList(newNode);
                return newNode;
            }
            this.InsertNodeAfter(this.tail, newNode);
            this.tail = newNode;
            return newNode;
        }

        public void AddLast(Iterable<T> list2)
        {
            if (list2 != null)
            {
                for (var current = list2.First; current != null; current = current.Next)
                {
                    var newNode = new ActiveListNode<T>((ActiveList<T>)this, current.Value);
                    if (this.head == null)
                    {
                        this.InsertNodeToEmptyList(newNode);
                    }
                    else
                    {
                        this.InsertNodeAfter(this.tail, newNode);
                        this.tail = newNode;
                    }
                }
            }
        }

        public void AddLast(ActiveListNode<T> node)
        {
            this.AssertNewNode(node);
            if (this.head == null)
            {
                this.InsertNodeToEmptyList(node);
            }
            else
            {
                this.InsertNodeAfter(this.tail, node);
                this.tail = node;
            }
        }

        public void Clear()
        {
            var head = this.head;
            while (head != null)
            {
                ActiveListNode<T> node2 = head;
                node2.Invalidate();
                head = head.Next;
            }
            this.head = null;
            this.count = 0;
        }

        public bool Contains(T value)
        {
            return (this.Find(value) != null);
        }

        public void CopyTo(T[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }
            if ((index < 0) || (index > array.Length))
            {
                throw new ArgumentOutOfRangeException("bad index " + index);
            }
            if ((array.Length - index) < this.Count)
            {
                throw new ArgumentException("Not enough space");
            }
            var head = this.head;
            if (head != null)
            {
                do
                {
                    array[index++] = head.item;
                    head = head.next;
                }
                while (head != this.head);
            }
        }

        public ActiveListNode<T> Find(T value)
        {
            var node = this.head;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (node != null)
            {
                if (value != null)
                {
                    do
                    {
                        if (comparer.Equals(node.item, value))
                        {
                            return node;
                        }
                        node = node.next;
                    }
                    while (node != null);
                }
                else
                {
                    do
                    {
                        if (node.item == null)
                        {
                            return node;
                        }
                        node = node.next;
                    }
                    while (node != null);
                }
            }
            return null;
        }

        private void InsertNodeBefore(ActiveListNode<T> node, ActiveListNode<T> newNode)
        {
            newNode.next = node;
            newNode.prev = node.prev;
            if( node.prev != null)
            {
                node.prev.next = newNode;
            }
            node.prev = newNode;
            newNode.list = (ActiveList<T>) this;
            ++this.count;
            ++version;
        }

        private void InsertNodeAfter(ActiveListNode<T> node, ActiveListNode<T> newNode)
        {
            newNode.prev = node;
            newNode.next = node.next;
            if( node.next != null)
            {
                node.next.prev = newNode;
            }
            node.next = newNode;
            newNode.list = (ActiveList<T>) this;
            ++this.count;
            ++version;
        }

        private void InsertNodeToEmptyList(ActiveListNode<T> newNode)
        {
            newNode.next = null;
            newNode.prev = null;
            this.head = newNode;
            this.tail = newNode;
            newNode.list = (ActiveList<T>) this;
            ++this.count;
            ++version;
        }

        private void RemoveNode(ActiveListNode<T> node)
        {
            if( node.next != null)
            {
                node.next.prev = node.prev;
            }
            if( node.prev != null)
            {
                node.prev.next = node.next;
            }
            node.Invalidate();
            if (this.head == node)
            {
                this.head = node.next;
            }
            if (this.tail == node)
            {
                this.tail = node.prev;
            }
            --this.count;
            ++version;
        }

        public bool Remove(T value)
        {
            var node = this.Find(value);
            if (node != null)
            {
                this.RemoveNode(node);
                return true;
            }
            return false;
        }

        public bool Remove(ActiveListNode<T> node)
        {
            if( node == null) {
                throw new ArgumentNullException("node");
            }
            if (node.list == null)
            {
                return false;
            }
            if (node.list != this)
            {
                throw new InvalidOperationException("node belongs to a different list. null? " + (node.list == null));
            }
            this.RemoveNode(node);
            return true;
        }

        public ActiveListNode<T> RemoveFirst()
        {
            if (this.head == null)
            {
                throw new InvalidOperationException("empty list");
            }
            var first = this.head;
            this.RemoveNode(first);
            return first;
        }

        public ActiveListNode<T> RemoveLast()
        {
            if (this.head == null)
            {
                throw new InvalidOperationException("empty list");
            }
            var last = this.tail;
            this.RemoveNode(last);
            return last;
        }

        internal void AssertNewNode(ActiveListNode<T> node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }
            if (node.list == this)
            {
                throw new InvalidOperationException("already in this list for " + node.Value);
            }
            if (node.list != null)
            {
                throw new InvalidOperationException("already in a different list for " + node.Value);
            }
        }

        internal void AssertNode(ActiveListNode<T> node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("active list node");
            }
            if (node.list == null)
            {
                if( head == node)
                {
                    throw new InvalidOperationException("node removed but head points to node " + node.Value);
                }
                if( count != 0)
                {
                    var current = head;
                    do
                    {
                        if( current.next == node)
                        {
                            throw new InvalidOperationException("node removed but a different node next still points to node for " + node.Value);
                        }
                        if( current.prev == node)
                        {
                            throw new InvalidOperationException("node removed but a different node prev still points to node for " + node.Value);
                        }
                        current = current.Next;
                    } while (current != null);
                }
                throw new InvalidOperationException("node not in the list for " + node.Value);
            }
            if (node.list != this)
            {
                if( node.list == null)
                {
                    throw new InvalidOperationException("wrong list. node.list is null for " + node.Value);
                }
                if (node.list != null)
                {
                    throw new InvalidOperationException("wrong list. mismatch \n node.list " + node.list.id + " " + node.list + "\n this " + this.id + " " + this);
                }
            }
        }

        // Properties
        public int Count
        {
            get { return this.count; }
        }

        public ActiveListNode<T> First
        {
            get { return this.head; }
        }

        public ActiveListNode<T> Last
        {
            get { return this.tail; }
        }

	    public long Version
	    {
	        get { return version; }
	    }
	}
}
