﻿#region copyright
/*
 * This code is from the Lawo/ember-plus GitHub repository and is licensed with
 *
 * Boost Software License - Version 1.0 - August 17th, 2003
 *
 * Permission is hereby granted, free of charge, to any person or organization
 * obtaining a copy of the software and accompanying documentation covered by
 * this license (the "Software") to use, reproduce, display, distribute,
 * execute, and transmit the Software, and to prepare derivative works of the
 * Software, and to permit third-parties to whom the Software is furnished to
 * do so, all subject to the following:
 *
 * The copyright notices in the Software and this entire statement, including
 * the above license grant, this restriction and the following disclaimer,
 * must be included in all copies of the Software, in whole or in part, and
 * all derivative works of the Software, unless such copies or derivative
 * works are solely in the form of machine-executable object code generated by
 * a source language processor.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
 * SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
 * FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */
 #endregion

using System;

namespace EmberPlusProviderClassLib.EmberHelpers
{
    /// <summary>
    /// This is a copy of the methods in EmberLib.Glow.InternalTools
    /// In the original the methods are internal and cannot be used
    /// outside of that library.
    /// The implementation is knowingly not updated to support
    /// Swedish letters, to keep identical behaviour as in other code.
    /// </summary>
    public class NodeAsserter
    {
        public static void AssertIdentifierValid(string identifier)
        {
            if (identifier == null)
                throw new ArgumentNullException("identifier");

            if (identifier.Length == 0)
                throw new ArgumentException("identifier must not be null!");

            if (IsValidIdentifierBegin(identifier[0]) == false)
                throw new ArgumentException("identifier must begin with a letter or underscore!");

            if (identifier.Contains("/"))
                throw new ArgumentException("identifier must not contain the '/' character!");
        }

        static bool IsValidIdentifierBegin(char ch)
        {
            return ch >= 'a' && ch <= 'z'
                   || ch >= 'A' && ch <= 'Z'
                   || ch == '_';
        }

    }
}