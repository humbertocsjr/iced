/*
Copyright (C) 2018-2019 de4dot@gmail.com

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

#if !NO_ENCODER
using System.Collections.Generic;
using Iced.Intel;
using Iced.UnitTests.Intel.DecoderTests;
using Xunit;

namespace Iced.UnitTests.Intel.EncoderTests {
	public sealed class EncoderTest_Invalid : EncoderTest {
		[Theory]
		[MemberData(nameof(EncodeInvalid_Data))]
		void EncodeInvalid(int codeSize, Code code, string hexBytes, DecoderOptions options, int invalidCodeSize) => EncodeInvalidBase(codeSize, code, hexBytes, options, invalidCodeSize);
		public static IEnumerable<object[]> EncodeInvalid_Data {
			get {
				foreach (var info in DecoderTestUtils.GetDecoderTests(includeOtherTests: false, includeInvalid: false)) {
					if (DecoderTestUtils.Code32Only.Contains(info.Code))
						yield return new object[] { info.Bitness, info.Code, info.HexBytes, info.Options, 64 };
					if (DecoderTestUtils.Code64Only.Contains(info.Code)) {
						yield return new object[] { info.Bitness, info.Code, info.HexBytes, info.Options, 16 };
						yield return new object[] { info.Bitness, info.Code, info.HexBytes, info.Options, 32 };
					}
				}
			}
		}
	}
}
#endif
