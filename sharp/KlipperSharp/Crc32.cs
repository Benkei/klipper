// Copyright (c) Damien Guard.  All rights reserved.
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. 
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Text;

namespace KlipperSharp
{
	/// <summary>
	/// Implements a 32-bit CRC hash algorithm compatible with Zip etc.
	/// </summary>
	/// <remarks>
	/// Crc32 should only be used for backward compatibility with older file formats
	/// and algorithms. It is not secure enough for new applications.
	/// If you need to call multiple times for the same data either use the HashAlgorithm
	/// interface or remember that the result of one Compute call needs to be ~ (XOR) before
	/// being passed in as the seed for the next Compute call.
	/// </remarks>
	public static class Crc32
	{
		public const uint DefaultPolynomial = 0xedb88320u;
		public const uint DefaultSeed = 0xffffffffu;

		static uint[] defaultTable;

		public static uint Compute(ReadOnlySpan<byte> buffer, uint polynomial= DefaultPolynomial, uint seed= DefaultSeed)
		{
			return ~CalculateHash(InitializeTable(polynomial), seed, buffer);
		}

		static uint[] InitializeTable(uint polynomial)
		{
			if (polynomial == DefaultPolynomial && defaultTable != null)
				return defaultTable;

			var createTable = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				var entry = i;
				for (var j = 0; j < 8; j++)
					if ((entry & 1) == 1)
						entry = (entry >> 1) ^ polynomial;
					else
						entry = entry >> 1;
				createTable[i] = entry;
			}

			if (polynomial == DefaultPolynomial)
				defaultTable = createTable;

			return createTable;
		}

		static uint CalculateHash(uint[] table, uint seed, ReadOnlySpan<byte> buffer)
		{
			var hash = seed;
			for (var i = 0; i < buffer.Length; i++)
				hash = (hash >> 8) ^ table[buffer[i] ^ hash & 0xff];
			return hash;
		}
	}
}
