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
using System.Collections.Generic;

namespace TickZoom.Api
{
	/// <summary>
	/// Description of FormulaChain.
	/// </summary>
	public class ChainImpl : Chain
	{
		ChainImpl previous = null;
		ChainImpl next = null;
		ModelInterface formula = null;
		bool isRoot = true;
		List<Chain> dependencies = new List<Chain>();
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(ChainImpl));
		private readonly bool debug = log.IsDebugEnabled;
		private readonly bool trace = log.IsTraceEnabled;
        public ChainImpl(ModelInterface formula)
		{
			if( trace) log.Trace("new ");
			if( trace) log.Indent();
			LinkFormula(formula);
			previous = this;
			next = this;
			if( trace) log.Outdent();
		}

		private ChainImpl()
		{
			previous = this;
			next = this;
		}

		public Chain GetAt(int index)
		{
			int i = 0;
			ChainImpl link = (ChainImpl) Root;
			do {
				if (i == index) {
					return link;
				}
				i++;
				link = link.next;
			}
			while (!link.isRoot);
			throw new ApplicationException("Not found at " + index);
		}

		public Chain InsertBefore(Chain chain)
		{
			if( trace) log.Trace(GetType().Name + " InsertBefore() " + chain.ToChainString() + " before " + this);
			ChainImpl root = (ChainImpl) chain.Root;
			ChainImpl tail = (ChainImpl) chain.Tail;
			root.isRoot = isRoot;
			isRoot = false;
			tail.next = this;
			root.previous = previous;
			previous.next = root;
			previous = tail;
			return root;
		}

		public Chain Replace(Chain chain)
		{
			if( trace) log.Trace(GetType().Name + " Replace() " + this + " with " + chain.ToChainString());
			ChainImpl root = (ChainImpl) chain.Root;
			ChainImpl tail = (ChainImpl) chain.Tail;
			root.isRoot = isRoot;
			isRoot = false;
			tail.next = next;
			root.previous = previous;
			previous.next = root;
			next.previous = tail;
			return root;
		}

		public void Remove()
		{
			previous.next = next;
			next.previous = previous;
		}
		
		private void LinkFormula(ModelInterface value)
		{
			formula = value;
			if (value != null) {
				value.Chain = this;
			}
		}

		public Chain InsertAfter(Chain chain)
		{
			if( trace) log.Trace(GetType().Name + " InsertAfter() " + chain.ToChainString() + " after " + this);
			ChainImpl root = (ChainImpl) chain.Root;
			ChainImpl tail = (ChainImpl) chain.Tail;
			root.isRoot = false;
			root.previous = this;
			tail.next = next;
			next.previous = tail;
			next = root;
			return root;
		}

		public string Name {
			get { return formula == null ? "(Null Name)" : formula.FullName + (isRoot ? "(root)" : ""); }
		}

		public Chain Root {
			get {
				ChainImpl current = previous;
				for (; !current.isRoot; current = current.previous)
;
				return current;
			}
		}

		public Chain Tail {
			get { return ((ChainImpl)Root).previous; }
		}

		public override string ToString()
		{
			return Name;
		}

		public string ToChainString()
		{
			ChainImpl root = (ChainImpl) Root;
			string output = "Chain=" + root.Name;
			for (ChainImpl current = root.next; !current.isRoot; current = current.next) {
				output += "," + current.Name;
			}
			return output;
		}

		public ModelInterface Model {
			get { return formula; }
		}
		public Chain Next {
			get { return next.isRoot ? new ChainImpl() : next; }
		}

		public Chain Previous {
			get { return this.isRoot ? new ChainImpl() : previous; }
		}

		public IList<Chain> Dependencies {
			get { return dependencies; }
		}

		public bool IsRoot {
			get { return isRoot; }
		}
	}
}
