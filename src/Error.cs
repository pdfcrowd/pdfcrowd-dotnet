// Copyright (C) 2009-2018 pdfcrowd.com
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
using System.Text.RegularExpressions;
using System.Net;

namespace pdfcrowd
{
    public class Error: Exception
    {
        string error = "";
        int http_code = 0;
        int reason_code = -1;
        string message = "";
        string doc_link = "";

        public Error(string _error)
        {
            error = _error;
        }

        public Error(string _error, int _http_code)
        {
            error = _error;

            var error_match = Regex.Match(
                error,
                @"^(\d+)\.(\d+)\s+-\s+(.*?)(?:\s+Documentation link:\s+(.*))?$",
            RegexOptions.Singleline);

            if (error_match.Success)
            {
                http_code = Int32.Parse(error_match.Groups[1].Value);
                reason_code = Int32.Parse(error_match.Groups[2].Value);
                message = error_match.Groups[3].Value;
                if(error_match.Groups[4].Success) {
                    doc_link = error_match.Groups[4].Value;
                }
            }
            else
            {
                http_code = _http_code;
                message = error;
                if(http_code != 0)
                {
                    error = $"{http_code} - {error}";
                }
            }
        }

        public Error(string _error, HttpStatusCode _http_code)
            : this(_error, (int) _http_code)
        {
        }

        public override string ToString()
        {
            return error;
        }

        [Obsolete("Use getStatusCode instead.")]
        public int getCode()
        {
            return http_code;
        }

        public int getStatusCode()
        {
            return http_code;
        }

        public int getReasonCode()
        {
            return reason_code;
        }

        public string getMessage()
        {
            return message;
        }

        public string getDocumentationLink()
        {
            return doc_link;
        }
    }
}
