﻿/*
    Copyright (C) 2018 de4dot@gmail.com

    This file is part of Iced.

    Iced is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Iced is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with Iced.  If not, see <https://www.gnu.org/licenses/>.
*/

#if !NO_INTEL_FORMATTER && !NO_FORMATTER
using System;
using System.Diagnostics;
using Iced.Intel.IntelFormatterInternal;

namespace Iced.Intel {
	/// <summary>
	/// Intel formatter options
	/// </summary>
	public sealed class IntelFormatterOptions : FormatterOptions {
		/// <summary>
		/// Constructor
		/// </summary>
		public IntelFormatterOptions() {
			HexSuffix = "h";
			OctalSuffix = "o";
			BinarySuffix = "b";
		}
	}

	/// <summary>
	/// Intel formatter (same as Intel XED)
	/// </summary>
	public sealed class IntelFormatter : Formatter {
		/// <summary>
		/// Gets the formatter options, see also <see cref="IntelOptions"/>
		/// </summary>
		public override FormatterOptions Options => options;

		/// <summary>
		/// Gets the Intel formatter options
		/// </summary>
		public IntelFormatterOptions IntelOptions => options;

		readonly IntelFormatterOptions options;
		readonly ISymbolResolver symbolResolver;
		readonly IFormatterOptionsProvider optionsProvider;
		readonly string[] allRegisters;
		readonly InstrInfo[] instrInfos;
		readonly (MemorySize memorySize, string[] names, string bcstTo)[] allMemorySizes;
		readonly NumberFormatter numberFormatter;

		/// <summary>
		/// Constructor
		/// </summary>
		public IntelFormatter() : this(new IntelFormatterOptions()) { }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="options">Formatter options</param>
		/// <param name="symbolResolver">Symbol resolver or null</param>
		/// <param name="optionsProvider">Operand options provider or null</param>
		public IntelFormatter(IntelFormatterOptions options, ISymbolResolver symbolResolver = null, IFormatterOptionsProvider optionsProvider = null) {
			this.options = options ?? throw new ArgumentNullException(nameof(options));
			this.symbolResolver = symbolResolver;
			this.optionsProvider = optionsProvider;
			allRegisters = Registers.AllRegisters;
			instrInfos = InstrInfos.AllInfos;
			allMemorySizes = MemorySizes.AllMemorySizes;
			numberFormatter = new NumberFormatter(options);
		}

		/// <summary>
		/// Formats the mnemonic and any prefixes
		/// </summary>
		/// <param name="instruction">Instruction</param>
		/// <param name="output">Output</param>
		public override void FormatMnemonic(ref Instruction instruction, FormatterOutput output) {
			Debug.Assert((uint)instruction.Code < (uint)instrInfos.Length);
			var instrInfo = instrInfos[(int)instruction.Code];
			instrInfo.GetOpInfo(options, ref instruction, out var opInfo);
			int column = 0;
			FormatMnemonic(ref instruction, output, ref opInfo, ref column);
		}

		/// <summary>
		/// Gets the number of operands that will be formatted. A formatter can add and remove operands
		/// </summary>
		/// <param name="instruction">Instruction</param>
		/// <returns></returns>
		public override int GetOperandCount(ref Instruction instruction) {
			Debug.Assert((uint)instruction.Code < (uint)instrInfos.Length);
			var instrInfo = instrInfos[(int)instruction.Code];
			instrInfo.GetOpInfo(options, ref instruction, out var opInfo);
			return opInfo.OpCount;
		}

		/// <summary>
		/// Formats an operand. This is a formatter operand and not necessarily a real instruction operand.
		/// A formatter can add and remove operands.
		/// </summary>
		/// <param name="instruction">Instruction</param>
		/// <param name="output">Output</param>
		/// <param name="operand">Operand number, 0-based. This is a formatter operand and isn't necessarily the same as an instruction operand.
		/// See <see cref="GetOperandCount(ref Instruction)"/></param>
		public override void FormatOperand(ref Instruction instruction, FormatterOutput output, int operand) {
			Debug.Assert((uint)instruction.Code < (uint)instrInfos.Length);
			var instrInfo = instrInfos[(int)instruction.Code];
			instrInfo.GetOpInfo(options, ref instruction, out var opInfo);

			if ((uint)operand >= (uint)opInfo.OpCount)
				throw new ArgumentOutOfRangeException(nameof(operand));
			FormatOperand(ref instruction, output, ref opInfo, operand);
		}

		/// <summary>
		/// Formats an operand separator
		/// </summary>
		/// <param name="instruction">Instruction</param>
		/// <param name="output">Output</param>
		public override void FormatOperandSeparator(ref Instruction instruction, FormatterOutput output) {
			output.Write(",", FormatterOutputTextKind.Punctuation);
			if (options.SpaceAfterOperandSeparator)
				output.Write(" ", FormatterOutputTextKind.Text);
		}

		/// <summary>
		/// Formats all operands
		/// </summary>
		/// <param name="instruction">Instruction</param>
		/// <param name="output">Output</param>
		public override void FormatAllOperands(ref Instruction instruction, FormatterOutput output) {
			Debug.Assert((uint)instruction.Code < (uint)instrInfos.Length);
			var instrInfo = instrInfos[(int)instruction.Code];
			instrInfo.GetOpInfo(options, ref instruction, out var opInfo);
			FormatOperands(ref instruction, output, ref opInfo);
		}

		/// <summary>
		/// Formats the whole instruction: prefixes, mnemonic, operands
		/// </summary>
		/// <param name="instruction">Instruction</param>
		/// <param name="output">Output</param>
		public override void Format(ref Instruction instruction, FormatterOutput output) {
			Debug.Assert((uint)instruction.Code < (uint)instrInfos.Length);
			var instrInfo = instrInfos[(int)instruction.Code];
			instrInfo.GetOpInfo(options, ref instruction, out var opInfo);

			int column = 0;
			FormatMnemonic(ref instruction, output, ref opInfo, ref column);

			if (opInfo.OpCount != 0) {
				FormatterUtils.AddTabs(output, column, options.FirstOperandCharIndex, options.TabSize);
				FormatOperands(ref instruction, output, ref opInfo);
			}
		}

		static readonly string[] opSizeStrings = new string[(int)InstrOpInfoFlags.SizeOverrideMask + 1] { null, "data16", "data32", "data64" };
		static readonly string[] addrSizeStrings = new string[(int)InstrOpInfoFlags.SizeOverrideMask + 1] { null, "addr16", "addr32", "addr64" };
		void FormatMnemonic(ref Instruction instruction, FormatterOutput output, ref InstrOpInfo opInfo, ref int column) {
			string prefix;

			prefix = opSizeStrings[((int)opInfo.Flags >> (int)InstrOpInfoFlags.OpSizeShift) & (int)InstrOpInfoFlags.SizeOverrideMask];
			if (prefix != null)
				FormatPrefix(output, ref column, prefix);

			prefix = addrSizeStrings[((int)opInfo.Flags >> (int)InstrOpInfoFlags.AddrSizeShift) & (int)InstrOpInfoFlags.SizeOverrideMask];
			if (prefix != null)
				FormatPrefix(output, ref column, prefix);

			var prefixSeg = instruction.SegmentPrefix;
			bool hasNoTrackPrefix = prefixSeg == Register.DS && FormatterUtils.IsNoTrackPrefixBranch(instruction.Code);
			if (!hasNoTrackPrefix && prefixSeg != Register.None && ShowSegmentPrefix(ref opInfo))
				FormatPrefix(output, ref column, allRegisters[(int)prefixSeg]);

			if (instruction.HasXacquirePrefix)
				FormatPrefix(output, ref column, "xacquire");
			if (instruction.HasXreleasePrefix)
				FormatPrefix(output, ref column, "xrelease");
			if (instruction.HasLockPrefix)
				FormatPrefix(output, ref column, "lock");

			if ((opInfo.Flags & InstrOpInfoFlags.JccNotTaken) != 0)
				FormatPrefix(output, ref column, "hint-not-taken");
			else if ((opInfo.Flags & InstrOpInfoFlags.JccTaken) != 0)
				FormatPrefix(output, ref column, "hint-taken");

			bool hasBnd = (opInfo.Flags & InstrOpInfoFlags.BndPrefix) != 0;
			if (instruction.HasRepePrefix)
				FormatPrefix(output, ref column, FormatterUtils.IsRepeOrRepneInstruction(instruction.Code) ? "repe" : "rep");
			if (instruction.HasRepnePrefix && !hasBnd)
				FormatPrefix(output, ref column, "repne");

			if (hasNoTrackPrefix)
				FormatPrefix(output, ref column, "notrack");

			if (hasBnd)
				FormatPrefix(output, ref column, "bnd");

			var mnemonic = opInfo.Mnemonic;
			if (options.UpperCaseMnemonics || options.UpperCaseAll)
				mnemonic = mnemonic.ToUpperInvariant();
			output.Write(mnemonic, FormatterOutputTextKind.Mnemonic);
			column += mnemonic.Length;
			if ((opInfo.Flags & InstrOpInfoFlags.FarMnemonic) != 0) {
				output.Write(" ", FormatterOutputTextKind.Text);
				var farKeyword = "far";
				FormatKeyword(output, farKeyword);
				column += farKeyword.Length + 1;
			}
		}

		bool ShowSegmentPrefix(ref InstrOpInfo opInfo) {
			if ((opInfo.Flags & InstrOpInfoFlags.IgnoreSegmentPrefix) != 0)
				return false;
			for (int i = 0; i < opInfo.OpCount; i++) {
				switch (opInfo.GetOpKind(i)) {
				case InstrOpKind.Register:
				case InstrOpKind.NearBranch16:
				case InstrOpKind.NearBranch32:
				case InstrOpKind.NearBranch64:
				case InstrOpKind.FarBranch16:
				case InstrOpKind.FarBranch32:
				case InstrOpKind.Immediate8:
				case InstrOpKind.Immediate8_2nd:
				case InstrOpKind.Immediate16:
				case InstrOpKind.Immediate32:
				case InstrOpKind.Immediate64:
				case InstrOpKind.Immediate8to16:
				case InstrOpKind.Immediate8to32:
				case InstrOpKind.Immediate8to64:
				case InstrOpKind.Immediate32to64:
				case InstrOpKind.MemoryESDI:
				case InstrOpKind.MemoryESEDI:
				case InstrOpKind.MemoryESRDI:
					break;

				case InstrOpKind.MemorySegSI:
				case InstrOpKind.MemorySegESI:
				case InstrOpKind.MemorySegRSI:
				case InstrOpKind.MemorySegDI:
				case InstrOpKind.MemorySegEDI:
				case InstrOpKind.MemorySegRDI:
				case InstrOpKind.Memory64:
				case InstrOpKind.Memory:
					return false;

				default:
					throw new InvalidOperationException();
				}
			}
			return true;
		}

		void FormatPrefix(FormatterOutput output, ref int column, string prefix) {
			if (options.UpperCasePrefixes || options.UpperCaseAll)
				prefix = prefix.ToUpperInvariant();
			output.Write(prefix, FormatterOutputTextKind.Prefix);
			output.Write(" ", FormatterOutputTextKind.Text);
			column += prefix.Length + 1;
		}

		void FormatOperands(ref Instruction instruction, FormatterOutput output, ref InstrOpInfo opInfo) {
			for (int i = 0; i < opInfo.OpCount; i++) {
				if (i > 0) {
					output.Write(",", FormatterOutputTextKind.Punctuation);
					if (options.SpaceAfterOperandSeparator)
						output.Write(" ", FormatterOutputTextKind.Text);
				}
				FormatOperand(ref instruction, output, ref opInfo, i);
			}
		}

		const int instructionOperand = -1;//TODO:
		void FormatOperand(ref Instruction instruction, FormatterOutput output, ref InstrOpInfo opInfo, int operand) {
			Debug.Assert((uint)operand < (uint)opInfo.OpCount);

			output.OnOperand(operand, begin: true);

			string s;
			FormatterFlowControl flowControl;
			byte imm8;
			ushort imm16;
			uint imm32;
			ulong imm64;
			int immSize;
			NumberFormattingOptions numberOptions;
			SymbolResult symbol;
			ISymbolResolver symbolResolver;
			FormatterOperandOptions operandOptions;
			var opKind = opInfo.GetOpKind(operand);
			switch (opKind) {
			case InstrOpKind.Register:
				FormatRegister(output, opInfo.GetOpRegister(operand));
				break;

			case InstrOpKind.NearBranch16:
			case InstrOpKind.NearBranch32:
			case InstrOpKind.NearBranch64:
				if (opKind == InstrOpKind.NearBranch64) {
					immSize = 8;
					imm64 = instruction.NearBranch64;
				}
				else if (opKind == InstrOpKind.NearBranch32) {
					immSize = 4;
					imm64 = instruction.NearBranch32;
				}
				else {
					immSize = 2;
					imm64 = instruction.NearBranch16;
				}
				numberOptions = NumberFormattingOptions.CreateBranch(options);
				operandOptions = options.ShowBranchSize ? FormatterOperandOptions.None : FormatterOperandOptions.NoBranchSize;
				optionsProvider?.GetOperandOptions(operand, instructionOperand, ref instruction, ref operandOptions, ref numberOptions);
				if ((symbolResolver = this.symbolResolver) != null && symbolResolver.TryGetSymbol(operand, instructionOperand, ref instruction, imm64, immSize, out symbol)) {
					FormatFlowControl(output, opInfo.Flags, operandOptions);
					output.Write(numberFormatter, ref numberOptions, imm64, symbol);
				}
				else {
					flowControl = FormatterUtils.GetFlowControl(ref instruction);
					FormatFlowControl(output, opInfo.Flags, operandOptions);
					if (opKind == InstrOpKind.NearBranch32)
						s = numberFormatter.FormatUInt32(ref numberOptions, instruction.NearBranch32, numberOptions.ShortNumbers);
					else if (opKind == InstrOpKind.NearBranch64)
						s = numberFormatter.FormatUInt64(ref numberOptions, instruction.NearBranch64, numberOptions.ShortNumbers);
					else
						s = numberFormatter.FormatUInt16(ref numberOptions, instruction.NearBranch16, numberOptions.ShortNumbers);
					output.Write(s, FormatterUtils.IsCall(flowControl) ? FormatterOutputTextKind.FunctionAddress : FormatterOutputTextKind.LabelAddress);
				}
				break;

			case InstrOpKind.FarBranch16:
			case InstrOpKind.FarBranch32:
				if (opKind == InstrOpKind.FarBranch32) {
					immSize = 4;
					imm64 = instruction.FarBranch32;
				}
				else {
					immSize = 2;
					imm64 = instruction.FarBranch16;
				}
				numberOptions = NumberFormattingOptions.CreateBranch(options);
				operandOptions = options.ShowBranchSize ? FormatterOperandOptions.None : FormatterOperandOptions.NoBranchSize;
				optionsProvider?.GetOperandOptions(operand, instructionOperand, ref instruction, ref operandOptions, ref numberOptions);
				if ((symbolResolver = this.symbolResolver) != null && symbolResolver.TryGetSymbol(operand, instructionOperand, ref instruction, (uint)imm64, immSize, out symbol)) {
					FormatFlowControl(output, opInfo.Flags, operandOptions);
					output.Write(numberFormatter, ref numberOptions, imm64, symbol);
					output.Write(",", FormatterOutputTextKind.Punctuation);
					if (options.SpaceAfterOperandSeparator)
						output.Write(" ", FormatterOutputTextKind.Text);
					Debug.Assert(operand + 1 == 1);
					if (!symbolResolver.TryGetSymbol(operand + 1, instructionOperand, ref instruction, instruction.FarBranchSelector, 2, out var selectorSymbol)) {
						s = numberFormatter.FormatUInt16(ref numberOptions, instruction.FarBranchSelector, numberOptions.ShortNumbers);
						output.Write(s, FormatterOutputTextKind.SelectorValue);
					}
					else
						output.Write(numberFormatter, ref numberOptions, instruction.FarBranchSelector, selectorSymbol);
				}
				else {
					flowControl = FormatterUtils.GetFlowControl(ref instruction);
					FormatFlowControl(output, opInfo.Flags, operandOptions);
					if (opKind == InstrOpKind.FarBranch32)
						s = numberFormatter.FormatUInt32(ref numberOptions, instruction.FarBranch32, numberOptions.ShortNumbers);
					else
						s = numberFormatter.FormatUInt16(ref numberOptions, instruction.FarBranch16, numberOptions.ShortNumbers);
					output.Write(s, FormatterUtils.IsCall(flowControl) ? FormatterOutputTextKind.FunctionAddress : FormatterOutputTextKind.LabelAddress);
					output.Write(",", FormatterOutputTextKind.Punctuation);
					if (options.SpaceAfterOperandSeparator)
						output.Write(" ", FormatterOutputTextKind.Text);
					s = numberFormatter.FormatUInt16(ref numberOptions, instruction.FarBranchSelector, numberOptions.ShortNumbers);
					output.Write(s, FormatterOutputTextKind.SelectorValue);
				}
				break;

			case InstrOpKind.Immediate8:
			case InstrOpKind.Immediate8_2nd:
				if (opKind == InstrOpKind.Immediate8)
					imm8 = instruction.Immediate8;
				else
					imm8 = instruction.Immediate8_2nd;
				numberOptions = NumberFormattingOptions.CreateImmediate(options);
				operandOptions = FormatterOperandOptions.None;
				optionsProvider?.GetOperandOptions(operand, instructionOperand, ref instruction, ref operandOptions, ref numberOptions);
				if ((symbolResolver = this.symbolResolver) != null && symbolResolver.TryGetSymbol(operand, instructionOperand, ref instruction, imm8, 1, out symbol))
					output.Write(numberFormatter, ref numberOptions, imm8, symbol);
				else {
					if (numberOptions.SignedNumber && (sbyte)imm8 < 0) {
						output.Write("-", FormatterOutputTextKind.Operator);
						imm8 = (byte)-(sbyte)imm8;
					}
					s = numberFormatter.FormatUInt8(ref numberOptions, imm8);
					output.Write(s, FormatterOutputTextKind.Number);
				}
				break;

			case InstrOpKind.Immediate16:
			case InstrOpKind.Immediate8to16:
				if (opKind == InstrOpKind.Immediate16)
					imm16 = instruction.Immediate16;
				else
					imm16 = (ushort)instruction.Immediate8to16;
				numberOptions = NumberFormattingOptions.CreateImmediate(options);
				operandOptions = FormatterOperandOptions.None;
				optionsProvider?.GetOperandOptions(operand, instructionOperand, ref instruction, ref operandOptions, ref numberOptions);
				if ((symbolResolver = this.symbolResolver) != null && symbolResolver.TryGetSymbol(operand, instructionOperand, ref instruction, imm16, 2, out symbol))
					output.Write(numberFormatter, ref numberOptions, imm16, symbol);
				else {
					if (numberOptions.SignedNumber && (short)imm16 < 0) {
						output.Write("-", FormatterOutputTextKind.Operator);
						imm16 = (ushort)-(short)imm16;
					}
					s = numberFormatter.FormatUInt16(ref numberOptions, imm16);
					output.Write(s, FormatterOutputTextKind.Number);
				}
				break;

			case InstrOpKind.Immediate32:
			case InstrOpKind.Immediate8to32:
				if (opKind == InstrOpKind.Immediate32)
					imm32 = instruction.Immediate32;
				else
					imm32 = (uint)instruction.Immediate8to32;
				numberOptions = NumberFormattingOptions.CreateImmediate(options);
				operandOptions = FormatterOperandOptions.None;
				optionsProvider?.GetOperandOptions(operand, instructionOperand, ref instruction, ref operandOptions, ref numberOptions);
				if ((symbolResolver = this.symbolResolver) != null && symbolResolver.TryGetSymbol(operand, instructionOperand, ref instruction, imm32, 4, out symbol))
					output.Write(numberFormatter, ref numberOptions, imm32, symbol);
				else {
					if (numberOptions.SignedNumber && (int)imm32 < 0) {
						output.Write("-", FormatterOutputTextKind.Operator);
						imm32 = (uint)-(int)imm32;
					}
					s = numberFormatter.FormatUInt32(ref numberOptions, imm32);
					output.Write(s, FormatterOutputTextKind.Number);
				}
				break;

			case InstrOpKind.Immediate64:
			case InstrOpKind.Immediate8to64:
			case InstrOpKind.Immediate32to64:
				if (opKind == InstrOpKind.Immediate32to64)
					imm64 = (ulong)instruction.Immediate32to64;
				else if (opKind == InstrOpKind.Immediate8to64)
					imm64 = (ulong)instruction.Immediate8to64;
				else
					imm64 = instruction.Immediate64;
				numberOptions = NumberFormattingOptions.CreateImmediate(options);
				operandOptions = FormatterOperandOptions.None;
				optionsProvider?.GetOperandOptions(operand, instructionOperand, ref instruction, ref operandOptions, ref numberOptions);
				if ((symbolResolver = this.symbolResolver) != null && symbolResolver.TryGetSymbol(operand, instructionOperand, ref instruction, imm64, 8, out symbol))
					output.Write(numberFormatter, ref numberOptions, imm64, symbol);
				else {
					if (numberOptions.SignedNumber && (long)imm64 < 0) {
						output.Write("-", FormatterOutputTextKind.Operator);
						imm64 = (ulong)-(long)imm64;
					}
					s = numberFormatter.FormatUInt64(ref numberOptions, imm64);
					output.Write(s, FormatterOutputTextKind.Number);
				}
				break;

			case InstrOpKind.MemorySegSI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, Register.SI, Register.None, 0, 0, 0, 2, opInfo.Flags);
				break;

			case InstrOpKind.MemorySegESI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, Register.ESI, Register.None, 0, 0, 0, 4, opInfo.Flags);
				break;

			case InstrOpKind.MemorySegRSI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, Register.RSI, Register.None, 0, 0, 0, 8, opInfo.Flags);
				break;

			case InstrOpKind.MemorySegDI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, Register.DI, Register.None, 0, 0, 0, 2, opInfo.Flags);
				break;

			case InstrOpKind.MemorySegEDI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, Register.EDI, Register.None, 0, 0, 0, 4, opInfo.Flags);
				break;

			case InstrOpKind.MemorySegRDI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, Register.RDI, Register.None, 0, 0, 0, 8, opInfo.Flags);
				break;

			case InstrOpKind.MemoryESDI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, Register.ES, Register.DI, Register.None, 0, 0, 0, 2, opInfo.Flags);
				break;

			case InstrOpKind.MemoryESEDI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, Register.ES, Register.EDI, Register.None, 0, 0, 0, 4, opInfo.Flags);
				break;

			case InstrOpKind.MemoryESRDI:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, Register.ES, Register.RDI, Register.None, 0, 0, 0, 8, opInfo.Flags);
				break;

			case InstrOpKind.Memory64:
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, Register.None, Register.None, 0, 8, (long)instruction.MemoryAddress64, 8, opInfo.Flags);
				break;

			case InstrOpKind.Memory:
				int displSize = instruction.MemoryDisplSize;
				var baseReg = instruction.MemoryBase;
				var indexReg = instruction.MemoryIndex;
				int addrSize = InstructionUtils.GetAddressSizeInBytes(baseReg, indexReg, displSize, instruction.CodeSize);
				long displ;
				if (addrSize == 8)
					displ = (int)instruction.MemoryDisplacement;
				else
					displ = instruction.MemoryDisplacement;
				if ((opInfo.Flags & InstrOpInfoFlags.IgnoreIndexReg) != 0)
					indexReg = Register.None;
				FormatMemory(output, ref instruction, operand, instruction.MemorySize, instruction.SegmentPrefix, instruction.MemorySegment, baseReg, indexReg, instruction.InternalMemoryIndexScale, displSize, displ, addrSize, opInfo.Flags);
				break;

			default:
				throw new InvalidOperationException();
			}

			if (operand == 0 && instruction.HasOpMask && (opInfo.Flags & InstrOpInfoFlags.IgnoreOpMask) == 0) {
				output.Write("{", FormatterOutputTextKind.Punctuation);
				FormatRegister(output, instruction.OpMask);
				output.Write("}", FormatterOutputTextKind.Punctuation);
				if (instruction.ZeroingMasking)
					FormatEvexMisc(output, "z");
			}
			if (operand == 0) {
				var rc = instruction.RoundingControl;
				if (rc != RoundingControl.None) {
					Debug.Assert((int)RoundingControl.None == 0);
					Debug.Assert((int)RoundingControl.RoundToNearest == 1);
					Debug.Assert((int)RoundingControl.RoundDown == 2);
					Debug.Assert((int)RoundingControl.RoundUp == 3);
					Debug.Assert((int)RoundingControl.RoundTowardZero == 4);
					var text = rcStrings[(int)rc];
					if (text != null)
						FormatEvexMisc(output, text);
				}
				else if (instruction.SuppressAllExceptions)
					FormatEvexMisc(output, "sae");
			}

			output.OnOperand(operand, begin: false);
		}
		static readonly string[] rcStrings = new string[] {
			null,
			"rne-sae",
			"rd-sae",
			"ru-sae",
			"rz-sae",
		};

		static readonly string[][] branchInfos = new string[(int)InstrOpInfoFlags.BranchSizeInfoMask + 1][] {
			null,
			new string[] { "short" },
		};
		void FormatFlowControl(FormatterOutput output, InstrOpInfoFlags flags, FormatterOperandOptions operandOptions) {
			if ((operandOptions & FormatterOperandOptions.NoBranchSize) != 0)
				return;
			var keywords = branchInfos[((int)flags >> (int)InstrOpInfoFlags.BranchSizeInfoShift) & (int)InstrOpInfoFlags.BranchSizeInfoMask];
			if (keywords == null)
				return;
			foreach (var keyword in keywords) {
				FormatKeyword(output, keyword);
				output.Write(" ", FormatterOutputTextKind.Text);
			}
		}

		void FormatEvexMisc(FormatterOutput output, string text) {
			if (options.UpperCaseOther || options.UpperCaseAll)
				text = text.ToUpperInvariant();
			output.Write("{", FormatterOutputTextKind.Punctuation);
			output.Write(text, FormatterOutputTextKind.Text);
			output.Write("}", FormatterOutputTextKind.Punctuation);
		}

		void FormatRegister(FormatterOutput output, Register reg) {
			Debug.Assert(reg != Register.None);
			Debug.Assert((uint)reg < (uint)allRegisters.Length);
			var regStr = allRegisters[(int)reg];
			if (options.UpperCaseRegisters || options.UpperCaseAll)
				regStr = regStr.ToUpperInvariant();
			output.Write(regStr, FormatterOutputTextKind.Register);
		}

		static readonly string[] scaleNumbers = new string[4] {
			"1", "2", "4", "8",
		};

		void FormatMemory(FormatterOutput output, ref Instruction instr, int operand, MemorySize memSize, Register segOverride, Register segReg, Register baseReg, Register indexReg, int scale, int displSize, long displ, int addrSize, InstrOpInfoFlags flags) {
			Debug.Assert((uint)scale < (uint)scaleNumbers.Length);
			Debug.Assert(InstructionUtils.GetAddressSizeInBytes(baseReg, indexReg, displSize, instr.CodeSize) == addrSize);

			var numberOptions = NumberFormattingOptions.CreateDisplacement(options);
			SymbolResult symbol;
			bool useSymbol;

			var operandOptions = (FormatterOperandOptions)((uint)options.MemorySizeOptions << (int)FormatterOperandOptions.MemorySizeShift);
			optionsProvider?.GetOperandOptions(operand, instructionOperand, ref instr, ref operandOptions, ref numberOptions);

			ulong absAddr;
			if (baseReg == Register.RIP) {
				absAddr = (ulong)((long)instr.NextIP64 + (int)displ);
				if (options.RipRelativeAddresses)
					operandOptions |= FormatterOperandOptions.RipRelativeAddresses;
			}
			else if (baseReg == Register.EIP) {
				absAddr = instr.NextIP32 + (uint)displ;
				if (options.RipRelativeAddresses)
					operandOptions |= FormatterOperandOptions.RipRelativeAddresses;
			}
			else {
				absAddr = (ulong)displ;
				operandOptions |= FormatterOperandOptions.RipRelativeAddresses;
			}

			var symbolResolver = this.symbolResolver;
			if (symbolResolver != null)
				useSymbol = symbolResolver.TryGetSymbol(operand, instructionOperand, ref instr, absAddr, addrSize, out symbol);
			else {
				useSymbol = false;
				symbol = default;
			}

			if ((operandOptions & FormatterOperandOptions.RipRelativeAddresses) == 0) {
				if (baseReg == Register.RIP) {
					Debug.Assert(indexReg == Register.None);
					baseReg = Register.None;
					displ = (long)absAddr;
					displSize = 8;
				}
				else if (baseReg == Register.EIP) {
					Debug.Assert(indexReg == Register.None);
					baseReg = Register.None;
					displ = (long)absAddr;
					displSize = 4;
				}
			}

			bool useScale = scale != 0 || options.AlwaysShowScale;
			if (!useScale) {
				// [rsi] = base reg, [rsi*1] = index reg
				if (baseReg == Register.None)
					useScale = true;
			}
			if (addrSize == 16)
				useScale = false;

			FormatMemorySize(output, ref instr, memSize, flags, operandOptions);

			if (options.AlwaysShowSegmentRegister || segOverride != Register.None) {
				FormatRegister(output, segReg);
				output.Write(":", FormatterOutputTextKind.Punctuation);
			}
			output.Write("[", FormatterOutputTextKind.Punctuation);
			if (options.SpaceAfterMemoryBracket)
				output.Write(" ", FormatterOutputTextKind.Text);

			bool needPlus = false;
			if (baseReg != Register.None) {
				FormatRegister(output, baseReg);
				needPlus = true;
			}

			if (indexReg != Register.None) {
				if (needPlus) {
					if (options.SpaceBetweenMemoryAddOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
					output.Write("+", FormatterOutputTextKind.Operator);
					if (options.SpaceBetweenMemoryAddOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
				}
				needPlus = true;

				if (!useScale)
					FormatRegister(output, indexReg);
				else if (options.ScaleBeforeIndex) {
					output.Write(scaleNumbers[scale], FormatterOutputTextKind.Number);
					if (options.SpaceBetweenMemoryMulOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
					output.Write("*", FormatterOutputTextKind.Operator);
					if (options.SpaceBetweenMemoryMulOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
					FormatRegister(output, indexReg);
				}
				else {
					FormatRegister(output, indexReg);
					if (options.SpaceBetweenMemoryMulOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
					output.Write("*", FormatterOutputTextKind.Operator);
					if (options.SpaceBetweenMemoryMulOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
					output.Write(scaleNumbers[scale], FormatterOutputTextKind.Number);
				}
			}

			if (useSymbol) {
				if (needPlus) {
					if (options.SpaceBetweenMemoryAddOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
					if ((symbol.Flags & SymbolFlags.Signed) != 0)
						output.Write("-", FormatterOutputTextKind.Operator);
					else
						output.Write("+", FormatterOutputTextKind.Operator);
					if (options.SpaceBetweenMemoryAddOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
				}
				else if ((symbol.Flags & SymbolFlags.Signed) != 0)
					output.Write("-", FormatterOutputTextKind.Operator);

				output.Write(numberFormatter, ref numberOptions, absAddr, symbol, false, Options.SpaceBetweenMemoryAddOperators);
			}
			else if (!needPlus || (displSize != 0 && (options.ShowZeroDisplacements || displ != 0))) {
				if (needPlus) {
					if (options.SpaceBetweenMemoryAddOperators)
						output.Write(" ", FormatterOutputTextKind.Text);

					if (addrSize == 4) {
						if (!numberOptions.SignedNumber)
							output.Write("+", FormatterOutputTextKind.Operator);
						else if ((int)displ < 0) {
							output.Write("-", FormatterOutputTextKind.Operator);
							displ = (uint)-(int)displ;
						}
						else
							output.Write("+", FormatterOutputTextKind.Operator);
						if (numberOptions.SignExtendImmediate) {
							Debug.Assert(displSize <= 4);
							displSize = 4;
						}
					}
					else if (addrSize == 8) {
						if (!numberOptions.SignedNumber)
							output.Write("+", FormatterOutputTextKind.Operator);
						else if (displ < 0) {
							output.Write("-", FormatterOutputTextKind.Operator);
							displ = -displ;
						}
						else
							output.Write("+", FormatterOutputTextKind.Operator);
						if (numberOptions.SignExtendImmediate) {
							Debug.Assert(displSize <= 8);
							displSize = 8;
						}
					}
					else {
						Debug.Assert(addrSize == 2);
						if (!numberOptions.SignedNumber)
							output.Write("+", FormatterOutputTextKind.Operator);
						else if ((short)displ < 0) {
							output.Write("-", FormatterOutputTextKind.Operator);
							displ = (ushort)-(short)displ;
						}
						else
							output.Write("+", FormatterOutputTextKind.Operator);
						if (numberOptions.SignExtendImmediate) {
							Debug.Assert(displSize <= 2);
							displSize = 2;
						}
					}
					if (options.SpaceBetweenMemoryAddOperators)
						output.Write(" ", FormatterOutputTextKind.Text);
				}

				string s;
				if (displSize <= 1 && (ulong)displ <= byte.MaxValue)
					s = numberFormatter.FormatUInt8(ref numberOptions, (byte)displ);
				else if (displSize <= 2 && (ulong)displ <= ushort.MaxValue)
					s = numberFormatter.FormatUInt16(ref numberOptions, (ushort)displ);
				else if (displSize <= 4 && (ulong)displ <= uint.MaxValue)
					s = numberFormatter.FormatUInt32(ref numberOptions, (uint)displ);
				else if (displSize <= 8)
					s = numberFormatter.FormatUInt64(ref numberOptions, (ulong)displ);
				else
					throw new InvalidOperationException();
				output.Write(s, FormatterOutputTextKind.Number);
			}

			if (options.SpaceAfterMemoryBracket)
				output.Write(" ", FormatterOutputTextKind.Text);
			output.Write("]", FormatterOutputTextKind.Punctuation);

			Debug.Assert((uint)memSize < (uint)allMemorySizes.Length);
			var bcstTo = allMemorySizes[(int)memSize].bcstTo;
			if (bcstTo != null)
				FormatEvexMisc(output, bcstTo);
		}

		void FormatMemorySize(FormatterOutput output, ref Instruction instr, MemorySize memSize, InstrOpInfoFlags flags, FormatterOperandOptions operandOptions) {
			var memSizeOptions = (MemorySizeOptions)(((uint)operandOptions >> (int)FormatterOperandOptions.MemorySizeShift) & ((uint)FormatterOperandOptions.MemorySizeMask >> (int)FormatterOperandOptions.MemorySizeShift));
			if (memSizeOptions == MemorySizeOptions.Never)
				return;

			if ((flags & InstrOpInfoFlags.MemSize_Nothing) != 0)
				return;

			if ((flags & InstrOpInfoFlags.ForceMemSizeDwordOrQword) != 0) {
				if (instr.CodeSize == CodeSize.Code16 || instr.CodeSize == CodeSize.Code32)
					memSize = MemorySize.UInt32;
				else
					memSize = MemorySize.UInt64;
			}

			Debug.Assert((uint)memSize < (uint)allMemorySizes.Length);
			var memInfo = allMemorySizes[(int)memSize];

			if (memSizeOptions == MemorySizeOptions.Default) {
				if ((flags & InstrOpInfoFlags.ShowNoMemSize_ForceSize) == 0 && memInfo.bcstTo == null)
					return;
			}
			else if (memSizeOptions == MemorySizeOptions.Minimum) {
				if ((flags & InstrOpInfoFlags.ShowMinMemSize_ForceSize) == 0)
					return;
			}
			else
				Debug.Assert(memSizeOptions == MemorySizeOptions.Always);

			foreach (string name in memInfo.names) {
				FormatKeyword(output, name);
				output.Write(" ", FormatterOutputTextKind.Text);
			}
		}

		void FormatKeyword(FormatterOutput output, string keyword) {
			if (options.UpperCaseKeywords || options.UpperCaseAll)
				keyword = keyword.ToUpperInvariant();
			output.Write(keyword, FormatterOutputTextKind.Keyword);
		}
	}
}
#endif
