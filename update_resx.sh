#!/bin/bash
for file in src/Aiursoft.Apkg/Resources/Views/Dashboard/Index.*.resx; do
    if [[ "$file" == *"zh-CN"* ]] || [[ "$file" == *"zh-TW"* ]] || [[ "$file" == *"zh-HK"* ]]; then
        sed -i 's/<\/root>//' "$file"
        cat << 'INNER_EOF' >> "$file"
  <data name="Search across &lt;strong class=&quot;text-white&quot;&gt;{0}&lt;/strong&gt; packages in &lt;strong class=&quot;text-white&quot;&gt;{1}&lt;/strong&gt; active repository" xml:space="preserve">
    <value>在 &lt;strong class="text-white"&gt;{1}&lt;/strong&gt; 个活跃仓库中搜索 &lt;strong class="text-white"&gt;{0}&lt;/strong&gt; 个包</value>
  </data>
  <data name="Search across &lt;strong class=&quot;text-white&quot;&gt;{0}&lt;/strong&gt; packages in &lt;strong class=&quot;text-white&quot;&gt;{1}&lt;/strong&gt; active repositories" xml:space="preserve">
    <value>在 &lt;strong class="text-white"&gt;{1}&lt;/strong&gt; 个活跃仓库中搜索 &lt;strong class="text-white"&gt;{0}&lt;/strong&gt; 个包</value>
  </data>
</root>
INNER_EOF
    else
        sed -i 's/<\/root>//' "$file"
        cat << 'INNER_EOF' >> "$file"
  <data name="Search across &lt;strong class=&quot;text-white&quot;&gt;{0}&lt;/strong&gt; packages in &lt;strong class=&quot;text-white&quot;&gt;{1}&lt;/strong&gt; active repository" xml:space="preserve">
    <value>Search across &lt;strong class="text-white"&gt;{0}&lt;/strong&gt; packages in &lt;strong class="text-white"&gt;{1}&lt;/strong&gt; active repository</value>
  </data>
  <data name="Search across &lt;strong class=&quot;text-white&quot;&gt;{0}&lt;/strong&gt; packages in &lt;strong class=&quot;text-white&quot;&gt;{1}&lt;/strong&gt; active repositories" xml:space="preserve">
    <value>Search across &lt;strong class="text-white"&gt;{0}&lt;/strong&gt; packages in &lt;strong class="text-white"&gt;{1}&lt;/strong&gt; active repositories</value>
  </data>
</root>
INNER_EOF
    fi
done
