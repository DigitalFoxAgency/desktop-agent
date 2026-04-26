-- Migration 0001: initial schema
-- Created: 2026-04-26
-- Owner: AgentDesktop platform
-- See specs/001-agent-platform-mvp/data-model.md "Persistence sketch".

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS conversations (
    id              TEXT PRIMARY KEY NOT NULL,
    title           TEXT NOT NULL,
    created_at      TEXT NOT NULL,
    last_activity_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_conversations_last_activity
    ON conversations (last_activity_at DESC);

CREATE TABLE IF NOT EXISTS messages (
    id                       TEXT PRIMARY KEY NOT NULL,
    conversation_id          TEXT NOT NULL,
    idx                      INTEGER NOT NULL,
    author                   INTEGER NOT NULL,
    body                     TEXT NOT NULL,
    created_at               TEXT NOT NULL,
    delegation_module_id     TEXT NULL,
    delegation_operation_id  TEXT NULL,
    skill_module_id          TEXT NULL,
    skill_id                 TEXT NULL,
    FOREIGN KEY (conversation_id) REFERENCES conversations (id) ON DELETE CASCADE,
    UNIQUE (conversation_id, idx),
    CHECK (
        (delegation_module_id IS NULL AND delegation_operation_id IS NULL)
        OR (delegation_module_id IS NOT NULL AND delegation_operation_id IS NOT NULL)
    ),
    CHECK (
        (skill_module_id IS NULL AND skill_id IS NULL)
        OR (skill_module_id IS NOT NULL AND skill_id IS NOT NULL)
    ),
    CHECK (
        NOT (delegation_module_id IS NOT NULL AND skill_module_id IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_messages_conversation_idx
    ON messages (conversation_id, idx);

CREATE TABLE IF NOT EXISTS policy_decisions (
    id                  TEXT PRIMARY KEY NOT NULL,
    kind                INTEGER NOT NULL,
    target              TEXT NOT NULL,
    origin_kind         INTEGER NOT NULL,
    origin_module_id    TEXT NOT NULL,
    origin_operation_id TEXT NULL,
    origin_skill_id     TEXT NULL,
    requested_at        TEXT NOT NULL,
    outcome             INTEGER NOT NULL,
    decided_at          TEXT NOT NULL,
    execution_result    INTEGER NULL,
    execution_error     TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_policy_decisions_decided_at
    ON policy_decisions (decided_at DESC);

CREATE TABLE IF NOT EXISTS audit_events (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at  TEXT NOT NULL,
    kind        TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_audit_events_created_at
    ON audit_events (created_at DESC);
