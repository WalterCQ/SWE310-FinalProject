import { createContext, useContext } from "react";

const PageRoleContext = createContext({
  setContextRole: () => {},
});

export function usePageRoleContext() {
  return useContext(PageRoleContext);
}

export default PageRoleContext;
