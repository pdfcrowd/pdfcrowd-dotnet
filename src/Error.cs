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
using System.Net;

namespace pdfcrowd
{
    public class Error: Exception
    {
        string error = "";
        int http_code = 0;

        public Error(string _error)
        {
            error = _error;
        }
        
        public Error(string _error, int _http_code)
        {
            error = _error;
            http_code = (int) _http_code;
        }

        public Error(string _error, HttpStatusCode _http_code)
            : this(_error, (int) _http_code)
        {
        }

        public override string ToString()
        {
            if( http_code != 0 )
            {
                return String.Format( "{0} - {1}", http_code, error );
            }
            return error;
        }

        public int getCode()
        {
            return http_code;
        }

        public string getMessage()
        {
            return error;
        }
    }
}
