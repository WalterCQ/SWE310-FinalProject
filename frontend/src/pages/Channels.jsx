import { useEffect, useState } from "react";
import { Hash, Send } from "lucide-react";
import {
  channels as channelsApi,
  formatApiError,
  messages as messagesApi,
  workspaces as workspacesApi,
} from "../api/taskflowApi.js";
import { asArray, mapChannel, mapMessage, mapWorkspace, selectPrimaryWorkspace } from "../api/mappers.js";

export default function Channels() {
  const [workspaceName, setWorkspaceName] = useState("");
  const [channels, setChannels] = useState([]);
  const [activeChannelId, setActiveChannelId] = useState("");
  const [messages, setMessages] = useState([]);
  const [draft, setDraft] = useState("");
  const [loading, setLoading] = useState(true);
  const [messageLoading, setMessageLoading] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;

    async function loadChannels() {
      setLoading(true);
      setError("");

      try {
        const workspaceItems = asArray(await workspacesApi.list()).map(mapWorkspace);
        const workspace = selectPrimaryWorkspace(workspaceItems);

        if (!workspace) {
          if (active) {
            setWorkspaceName("");
            setChannels([]);
            setActiveChannelId("");
          }
          return;
        }

        const channelItems = asArray(await channelsApi.listByWorkspace(workspace.id)).map(mapChannel);

        if (active) {
          setWorkspaceName(workspace.name);
          setChannels(channelItems);
          setActiveChannelId(channelItems[0]?.id || "");
        }
      } catch (apiError) {
        if (active) setError(formatApiError(apiError));
      } finally {
        if (active) setLoading(false);
      }
    }

    loadChannels();

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    let active = true;

    async function loadMessages() {
      if (!activeChannelId) {
        setMessages([]);
        return;
      }

      setMessageLoading(true);
      setError("");

      try {
        const messageItems = asArray(await messagesApi.listByChannel(activeChannelId)).map(mapMessage);
        if (active) setMessages(messageItems);
      } catch (apiError) {
        if (active) setError(formatApiError(apiError));
      } finally {
        if (active) setMessageLoading(false);
      }
    }

    loadMessages();

    return () => {
      active = false;
    };
  }, [activeChannelId]);

  async function sendMessage(event) {
    event.preventDefault();
    const content = draft.trim();
    if (!content || !activeChannelId) return;

    setError("");

    try {
      await messagesApi.create(activeChannelId, { content });
      setDraft("");
      const messageItems = asArray(await messagesApi.listByChannel(activeChannelId)).map(mapMessage);
      setMessages(messageItems);
    } catch (apiError) {
      setError(formatApiError(apiError));
    }
  }

  const activeChannel = channels.find((channel) => channel.id === activeChannelId);

  return (
    <div className="page-stack">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Channels</p>
          <h1>Team chat</h1>
        </div>
      </div>

      {loading && <section className="panel">Loading channels from Azure...</section>}
      {error && <section className="panel"><strong>Unable to load chat data.</strong><p>{error}</p></section>}
      {!loading && !error && !workspaceName && (
        <section className="panel">No workspace data is available yet.</section>
      )}

      {!loading && !error && workspaceName && (
        <section className="chat-layout">
          <aside className="panel channel-list">
            {channels.length === 0 && <p>No channels found.</p>}
            {channels.map((channel) => (
              <button
                key={channel.id}
                className={channel.id === activeChannelId ? "active" : ""}
                onClick={() => setActiveChannelId(channel.id)}
              >
                <Hash size={16} /> {channel.name}
              </button>
            ))}
          </aside>

          <article className="panel chat-panel">
            <div className="panel-header">
              <h3>{activeChannel ? `# ${activeChannel.name}` : "No channel selected"}</h3>
              <span>{activeChannel ? `${activeChannel.memberCount} members` : workspaceName}</span>
            </div>
            <div className="message-list">
              {messageLoading && <p>Loading messages...</p>}
              {!messageLoading && activeChannel && messages.length === 0 && <p>No messages in this channel yet.</p>}
              {!messageLoading && messages.map((message, index) => (
                <div className="message-row" key={message.id}>
                  <div className="activity-index message-index">{String(index + 1).padStart(2, "0")}</div>
                  <div>
                    <strong>{message.sender} <span>{message.time}</span></strong>
                    <p>{message.text}</p>
                  </div>
                </div>
              ))}
            </div>
            <form className="message-input" onSubmit={sendMessage}>
              <input
                placeholder={activeChannel ? `Message #${activeChannel.name}` : "Select a channel"}
                value={draft}
                onChange={(event) => setDraft(event.target.value)}
                disabled={!activeChannel}
              />
              <button disabled={!activeChannel || !draft.trim()}><Send size={18} /></button>
            </form>
          </article>
        </section>
      )}
    </div>
  );
}
