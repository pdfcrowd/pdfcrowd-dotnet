// Copyright (C) 2009-2013 pdfcrowd.com
// 
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using pdfcrowd;

namespace tests
{
    class Program
    {
      private static void run_test( Tests test_obj, 
                                    string name,
                                    bool use_ssl)
      {
        System.Console.WriteLine(name + ", ssl:" + (use_ssl ? "yes" : "no"));
        MethodInfo method = typeof(Tests).GetMethod(name);
        object[] args = new object[1];
        args[0] = use_ssl;
        method.Invoke( test_obj, args);
      }
      
      static void Main(string[] args)
      {
        string[] test_functions = 
          { 
            "TestConvertByURI",
            "TestConvertFile",
            "TestConvertHtml",
            "TestTokens",
            "TestStreams",
            "TestMore"
          };
        
        Tests t = new Tests(args);
        for(int i=0; i<test_functions.Length; i++)
          {
            run_test(t, test_functions[i], false);
            if (t.client.HOST == "pdfcrowd.com")
              run_test(t, test_functions[i], true);
          }
      }
    }
}
