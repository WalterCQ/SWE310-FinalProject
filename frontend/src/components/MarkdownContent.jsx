import ReactMarkdown from "react-markdown";

export default function MarkdownContent({ children, className = "" }) {
  const markdown = typeof children === "string" ? children : "";
  const classes = ["markdown-content", className].filter(Boolean).join(" ");

  return (
    <div className={classes}>
      <ReactMarkdown
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
        }}
      >
        {markdown}
      </ReactMarkdown>
    </div>
  );
}
