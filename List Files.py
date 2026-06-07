import os
from pathlib import Path

def list_file_names(source_dir: str, output_filename: str = "combined_files.txt"):
    source_path = Path(source_dir).resolve()
    if not source_path.exists():
        print(f"Error: Target directory '{source_path}' does not exist.")
        return
        
    output_path = Path(output_filename).resolve()
    allowed_extensions = {".cs", ".csproj", ".md", ".txt"}
    ignored_dirs = {".git", "bin", "obj", ".vs", "packages", "node_modules"}
    
    print(f"Scanning directory: {source_path}")
    print(f"Target extensions: {', '.join(allowed_extensions)}")
    
    file_count = 0
    
    with open(output_path, "w", encoding="utf-8") as outfile:
        for root, dirs, files in os.walk(source_path):
            # Exclude build and metadata folders
            dirs[:] = [d for d in dirs if d.lower() not in ignored_dirs]
            
            for file in files:
                file_ext = Path(file).suffix.lower()
                if file_ext in allowed_extensions:
                    full_file_path = Path(root) / file
                    
                    # Prevent listing the output file itself if it falls inside the scan path
                    if full_file_path.resolve() == output_path:
                        continue
                        
                    # Write ONLY the file name (e.g., "FieldSerializer.cs")
                    outfile.write(f"{file}\n")
                    print(f"Listed: {file}")
                    file_count += 1
                    
    print(f"\nDone! Listed {file_count} file names.")
    print(f"File names saved to: {output_path}")

if __name__ == "__main__":
    target_folder = r"G:\GitHub\AssetRipper-New\Source"
    
    # Fallback to current directory if the specific folder is not found
    if not os.path.exists(target_folder):
        print(f"Warning: Specific folder '{target_folder}' not found. Falling back to current directory.")
        target_folder = "."
        
    list_file_names(target_folder)