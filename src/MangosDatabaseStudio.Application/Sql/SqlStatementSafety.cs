// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Classify SQL using a quote- and comment-aware token stream, failing closed on ambiguity.

using System;
using System.Text;

namespace MangosDatabaseStudio.Application.Sql
{
    public sealed class SqlStatementSafety
    {
        public bool RequiresDestructivePermission
        {
            get;
            init;
        }

        public bool ContainsMutation
        {
            get;
            init;
        }

        public string MutationOperation
        {
            get;
            init;
        } = string.Empty;

        public int StatementCount
        {
            get;
            init;
        }

        // Create a reusable streaming classifier for a SQL import or in-memory command.
        public static SqlStatementScanner CreateScanner()
        {
            return new SqlStatementScanner();
        }

        // Require elevated permission for unrecognized statements and multi-statement console input.
        public static SqlStatementSafety Analyze(string? sql)
        {
            SqlStatementScanner scanner = CreateScanner();
            scanner.Append(sql ?? string.Empty);
            return scanner.Complete(requireSingleStatement: true);
        }
    }

    public sealed class SqlStatementScanner
    {
        private enum ScanState
        {
            Sql,
            SingleQuoted,
            DoubleQuoted,
            BacktickQuoted,
            LineComment,
            BlockComment
        }

        private readonly StringBuilder _word = new();
        private ScanState _state;
        private string _tail = string.Empty;
        private string _firstWord = string.Empty;
        private string _mutationOperation = string.Empty;
        private bool _statementHasContent;
        private bool _statementUnsafe;
        private bool _statementMutates;
        private bool _hasUnsafeStatement;
        private bool _hasMutation;
        private bool _escaped;
        private bool _completed;
        private int _statementCount;

        // Read SQL incrementally without retaining the entire imported file in memory.
        public void Append(ReadOnlySpan<char> text)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The SQL scanner has already completed.");
            }

            string combined = _tail + text.ToString();
            int safeLength = Math.Max(0, combined.Length - 4);
            int consumed = Process(combined, safeLength);
            _tail = combined[consumed..];
        }

        // Finalize pending words and reject unterminated strings or comments.
        public SqlStatementSafety Complete(bool requireSingleStatement = false)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The SQL scanner has already completed.");
            }

            _completed = true;
            Process(_tail, _tail.Length);
            _tail = string.Empty;
            FlushWord();

            if (_state != ScanState.Sql && _state != ScanState.LineComment)
            {
                _statementUnsafe = true;
                _statementHasContent = true;
            }

            FinishStatement();
            return new SqlStatementSafety
            {
                RequiresDestructivePermission = _hasUnsafeStatement ||
                    _statementCount == 0 || (requireSingleStatement && _statementCount != 1),
                ContainsMutation = _hasMutation || _hasUnsafeStatement,
                MutationOperation = _mutationOperation.Length == 0 && _hasUnsafeStatement
                    ? "UNCLASSIFIED"
                    : _mutationOperation,
                StatementCount = _statementCount
            };
        }

        // Consume tokens while respecting MySQL quoted strings, identifiers, and comments.
        private int Process(string sql, int limit)
        {
            int i = 0;
            for (; i < limit; i++)
            {
                char c = sql[i];
                char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

                if (_state == ScanState.LineComment)
                {
                    if (c == '\r' || c == '\n')
                    {
                        _state = ScanState.Sql;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (_state == ScanState.BlockComment)
                {
                    if (c == '/' && next == '*')
                    {
                        // MySQL nested comments are not consistently supported across versions.
                        _statementUnsafe = true;
                    }

                    if (c == '*' && next == '/')
                    {
                        i++;
                        _state = ScanState.Sql;
                    }

                    continue;
                }

                if (_state == ScanState.SingleQuoted || _state == ScanState.DoubleQuoted || _state == ScanState.BacktickQuoted)
                {
                    char delimiter = _state == ScanState.SingleQuoted ? '\'' :
                        _state == ScanState.DoubleQuoted ? '"' : '`';

                    if (_escaped)
                    {
                        _escaped = false;
                    }
                    else if (c == '\\' && _state != ScanState.BacktickQuoted)
                    {
                        // NO_BACKSLASH_ESCAPES changes quote boundaries; require elevated permission.
                        _statementUnsafe = true;
                        _escaped = true;
                    }
                    else if (c == delimiter)
                    {
                        if (next == delimiter)
                        {
                            i++;
                        }
                        else
                        {
                            _state = ScanState.Sql;
                        }
                    }

                    continue;
                }

                if (c == '/' && next == '*')
                {
                    FlushWord();
                    if ((i + 2 < sql.Length && sql[i + 2] == '!') ||
                        (i + 3 < sql.Length && (sql[i + 2] == 'M' || sql[i + 2] == 'm') && sql[i + 3] == '!'))
                    {
                        // Versioned MySQL/MariaDB comments may execute SQL inside the comment.
                        _statementUnsafe = true;
                        _statementHasContent = true;
                    }

                    _state = ScanState.BlockComment;
                    i++;
                    continue;
                }

                if (c == '#' || (c == '-' && next == '-' &&
                    i + 2 < sql.Length && char.IsWhiteSpace(sql[i + 2])))
                {
                    FlushWord();
                    _state = ScanState.LineComment;
                    if (c == '-')
                    {
                        i++;
                    }

                    continue;
                }

                if (c == '\'' || c == '"' || c == '`')
                {
                    FlushWord();
                    _statementHasContent = true;
                    _state = c == '\'' ? ScanState.SingleQuoted :
                        c == '"' ? ScanState.DoubleQuoted : ScanState.BacktickQuoted;
                    continue;
                }

                if (char.IsLetterOrDigit(c) || c == '_' || c == '$')
                {
                    if (_word.Length < 64)
                    {
                        _word.Append(char.ToUpperInvariant(c));
                    }
                    else
                    {
                        // Pathological identifiers must not exhaust memory during large imports.
                        _statementUnsafe = true;
                    }

                    _statementHasContent = true;
                    continue;
                }

                FlushWord();
                if (c == ';')
                {
                    FinishStatement();
                }
                else if (!char.IsWhiteSpace(c))
                {
                    _statementHasContent = true;
                }
            }

            return i;
        }

        // Recognize only simple, explicitly permitted statement families.
        private void FlushWord()
        {
            if (_word.Length == 0)
            {
                return;
            }

            string word = _word.ToString();
            _word.Clear();
            if (_firstWord.Length == 0)
            {
                _firstWord = word;
            }

            if (word is "INSERT" or "UPDATE" or "REPLACE" or "DELETE" or "CREATE" or "ALTER" or
                "DROP" or "TRUNCATE" or "RENAME" or "GRANT" or "REVOKE" or "LOAD" or "CALL" or
                "SET" or "USE" or "LOCK" or "UNLOCK" or "FLUSH" or "RESET" or "HANDLER" or
                "PREPARE" or "EXECUTE" or "DEALLOCATE" or "DO" or "KILL" or "INTO" or
                "OUTFILE" or "DUMPFILE" or "LOAD_FILE" or "GET_LOCK" or "RELEASE_LOCK")
            {
                if (_mutationOperation.Length == 0 && word != "INTO")
                {
                    _mutationOperation = word;
                }

                bool permittedMutation = word is "INSERT" or "UPDATE" or "REPLACE";
                bool ordinaryInsertTarget = word == "INTO" &&
                    (_firstWord == "INSERT" || _firstWord == "REPLACE");
                // SET is the normal assignment clause in UPDATE, but a standalone
                // SET statement still requires elevated SQL permissions.
                bool ordinaryUpdateAssignment = word == "SET" && _firstWord == "UPDATE";
                if ((!permittedMutation && !ordinaryInsertTarget && !ordinaryUpdateAssignment) ||
                    (permittedMutation && _firstWord != "INSERT" &&
                        _firstWord != "UPDATE" && _firstWord != "REPLACE"))
                {
                    _statementUnsafe = true;
                }

                _statementMutates = true;
            }
        }

        // End each statement separately so import files do not require full buffering.
        private void FinishStatement()
        {
            if (!_statementHasContent && _firstWord.Length == 0 && !_statementUnsafe)
            {
                return;
            }

            _statementCount++;
            bool known = _firstWord is "SELECT" or "SHOW" or "DESC" or "DESCRIBE" or
                "EXPLAIN" or "INSERT" or "UPDATE" or "REPLACE";
            bool mutating = _statementMutates || !known;
            _hasMutation |= mutating;
            _hasUnsafeStatement |= _statementUnsafe || !known;
            if (mutating && _mutationOperation.Length == 0)
            {
                _mutationOperation = _firstWord.Length > 0 ? _firstWord : "UNCLASSIFIED";
            }

            _firstWord = string.Empty;
            _statementHasContent = false;
            _statementUnsafe = false;
            _statementMutates = false;
        }
    }
}
