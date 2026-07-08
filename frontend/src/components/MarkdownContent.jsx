import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

export default function MarkdownContent({ children, className = "" }) {
  const markdown = typeof children === "string" ? children : "";
  const classes = ["markdown-content", className].filter(Boolean).join(" ");

  return (
    <div className={classes}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          a({ node, href = "", ...props }) {
            const external = /^https?:\/\//i.test(href);
            return (
              <a
                href={href}
                rel={external ? "noreferrer" : undefined}
                target={external ? "_blank" : undefined}
                {...props}
              />
            );
          },
          table({ node, ...props }) {
            return (
              <div className="markdown-table-wrap">
                <table {...props} />
              </div>
            );
          },
        }}
      >
        {markdown}
      </ReactMarkdown>
    </div>
  );
}
